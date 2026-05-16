using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Application.Parts;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Debug;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.Rd;
using JetBrains.Rd.Base;
using JetBrains.ReSharper.Feature.Services.ExternalSource;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Utils;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Util;
using JetBrains.Util.Logging;
using RiderIlSpy.Model;

namespace RiderIlSpy;

[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public class IlSpyExternalSourcesProvider : IExternalSourcesProvider
{
    private const string DecompilerId = "RiderIlSpy";

    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyExternalSourcesProvider>();

    private readonly INavigationDecompilationCache myCache;
    private readonly IlSpyDecompiler myDecompiler;
    private readonly IlSpySourceLinkGateway mySourceLinkGateway;
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly IShellLocks myShellLocks;
    private readonly Lifetime myLifetime;
    private readonly TypeEntryCache myEntryCache = new TypeEntryCache();
    private readonly IlSpyRequestSettingsBuilder mySettingsBuilder;
    private readonly RiderIlSpyModel myRiderIlSpyModel;
    private readonly ModeChangeRedecompiler myModeChangeRedecompiler;

    public IlSpyExternalSourcesProvider(
        Lifetime lifetime,
        ISolution solution,
        ISettingsStore settingsStore,
        INavigationDecompilationCache cache,
        IlSpyDecompiler decompiler,
        IlSpySourceLinkGateway sourceLinkGateway,
        IShellLocks shellLocks)
    {
        myCache = cache;
        myDecompiler = decompiler;
        mySourceLinkGateway = sourceLinkGateway;
        myShellLocks = shellLocks;
        myLifetime = lifetime;
        mySettings = settingsStore.BindToContextLive(lifetime, ContextRange.ApplicationWide);
        mySettingsBuilder = new IlSpyRequestSettingsBuilder(mySettings, ourLogger);
        myRiderIlSpyModel = solution.GetProtocolSolution().GetRiderIlSpyModel();
        // Mode-change choreography (debounce + cancel-supersede + protocol-thread
        // readyTick fire) lives in ModeChangeRedecompiler so this ctor's
        // responsibilities stay limited to wiring rd subscriptions.
        myModeChangeRedecompiler = new ModeChangeRedecompiler(
            RedecompileAllEntriesAsync,
            FireReadyTickOnProtocolThread,
            ourLogger.Verbose,
            ourLogger.Error);
        myRiderIlSpyModel.Mode.Advise(lifetime, myModeChangeRedecompiler.OnModeChanged);
        // Save-as-project rd handler moved to SaveAsProjectProtocolHandler — it
        // owns its own [SolutionComponent] subscription so the navigation
        // provider doesn't pull in the protocol-handler concern.
    }

    public string PresentableShortName => "ILSpy";
    public string Id => DecompilerId;
    public int Priority => 100;

    public bool IsApplicableForNavigation(CompiledElementNavigationInfo? navigationInfo, bool ignoreOptions)
    {
        return IsIlSpyEnabled();
    }

    public bool IsPreferredForNavigation()
    {
        return IsIlSpyEnabled();
    }

    private bool IsIlSpyEnabled()
    {
        return mySettings.GetValue((IlSpySettings s) => s.Enabled);
    }

    // BuildDecompilerSettings, GetExtraSearchDirs, and the TryNormalizeSearchDir
    // wrapper moved to IlSpyRequestSettingsBuilder so the SaveAsProjectProtocolHandler
    // can share the exact settings-build pipeline without duplicating reads.

    public ExternalSourcesMapping? MapFileToAssembly(FileSystemPath file)
    {
        if (!myCache.CanBeCachedFile(Id, file)) return null;
        DecompilationCacheItem? item = myCache.GetCacheItem(file);
        if (item == null) return null;
        TryRehydrateEntry(item);
        return new ExternalSourcesMapping(item.Assembly, item.Location, this, isUserFile: false);
    }

    private void TryRehydrateEntry(DecompilationCacheItem item)
    {
        try
        {
            TypeDecompileEntry? entry = TryParseEntry(item.Properties, item.Assembly);
            if (entry == null) return;
            if (myEntryCache.Contains(entry.Moniker)) return;
            myEntryCache.Track(entry.Moniker, entry);
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.TryRehydrateEntry failed");
        }
    }

    public IReadOnlyCollection<ExternalSourcesMapping> NavigateToSources(ICompiledElement compiledElement, ITaskExecutor taskExecutor)
    {
        DecompilationCacheItem? item = DecompileToCacheItem(compiledElement, taskExecutor);
        if (item == null) return ImmutableArray<ExternalSourcesMapping>.Empty;
        return ImmutableArray.Create(new ExternalSourcesMapping(item.Assembly, item.Location, this, isUserFile: false));
    }

    public IReadOnlyCollection<ExternalSourcesMapping> NavigateToSources(CompiledElementNavigationInfo navigationInfo, ITaskExecutor taskExecutor)
    {
        return NavigateToSources(navigationInfo.ElementToSearchIn, taskExecutor);
    }

    // RiderIlSpy doesn't synthesize a PDB for its decompiled output — ILSpy emits
    // source text, not portable debug info. ExtendedDebugData would need line-mapping
    // back to the original assembly's PDB, which we don't track per cache entry.
    // The honest contract is "we have nothing extra to offer here". Returning false
    // from IsPreferredForGettingDebugData lets Rider fall back to the platform's
    // default debug-data flow for our cached files rather than wasting a call into
    // GetTypeDebugData / GetSourceDebugData that will always answer null.
    public ExtendedDebugData? GetTypeDebugData(ICompiledElement type, ITaskExecutor taskExecutor) => null;

    public ExtendedDebugData? GetSourceDebugData(FileSystemPath file) => null;

    public bool IsPreferredForGettingDebugData(FileSystemPath file) => false;

    private DecompilationCacheItem? DecompileToCacheItem(ICompiledElement compiledElement, ITaskExecutor taskExecutor)
    {
        try
        {
            ITypeElement? top = GetTopLevelTypeElement(compiledElement);
            if (top == null) return null;

            IAssembly? assembly = top.Module.ContainingProjectModule as IAssembly;
            if (assembly == null) return null;

            FileSystemPath? assemblyFile = ResolveAssemblyFile(assembly);
            if (assemblyFile == null) return null;

            IClrTypeName? clrName = top.GetClrName();
            if (clrName == null) return null;
            string fullName = clrName.FullName;
            if (string.IsNullOrEmpty(fullName)) return null;

            IlSpyRequestSettings request = SnapshotRequestSettings();
            string moniker = MonikerUtil.GetTypeCacheMoniker(top);
            string fileName = (top.ShortName ?? "Decompiled") + ".cs";

            TypeDecompileEntry? trackedEntry = myEntryCache.TryGet(moniker);
            bool sameMode = trackedEntry != null && trackedEntry.Mode == request.Mode;
            DecompilationCacheItem? cached = myCache.GetCacheItem(Id, assembly, moniker, fileName);
            if (cached != null && !cached.Expired && sameMode) return cached;

            DecompileFetchOutcome? fetch = FetchDecompiledContent(
                assemblyFile.FullPath,
                fullName,
                taskTitle: "Decompiling " + top.ShortName + " with ILSpy",
                request,
                taskExecutor);
            if (fetch == null) return null;

            string content = fetch.Content;
            // Banner is only meaningful for ILSpy output. SourceLink already
            // returns the upstream file verbatim — prepending decompile
            // diagnostics there would just shift the real line numbers down
            // (annoying for "go to line N" navigation) without adding signal.
            if (!fetch.FromSourceLink)
            {
                AssemblyBannerMetadata? bannerMeta = request.ShowBanner ? ReadBannerMetadata(assemblyFile.FullPath) : null;
                BannerContext bannerCtx = new BannerContext(bannerMeta, assemblyFile.FullPath, fullName, request.Mode, request.ExtraSearchDirs);
                content = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(request.ShowBanner, bannerCtx, fetch.SourceLinkOutcome, content);
            }
            DecompilationCacheItem? result = WriteToCache(assembly, assemblyFile.FullPath, fullName, moniker, fileName, request.Mode, content);
            if (result != null)
            {
                myEntryCache.Track(moniker, new TypeDecompileEntry(assembly, assemblyFile.FullPath, fullName, moniker, fileName, request.Mode));
            }
            return result;
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.DecompileToCacheItem failed");
            return null;
        }
    }

    /// <summary>
    /// Picks the on-disk assembly file for <paramref name="assembly"/>, preferring
    /// the implementation assembly over reference assemblies (the `/ref/` path
    /// segment is the standard SDK marker for ref-only assemblies). Reference
    /// assemblies contain no IL bodies, so decompiling one yields stubs with empty
    /// method bodies — useless for source navigation. Falls back to the first
    /// existing file when only ref assemblies are present, so we at least return
    /// stubs instead of null.
    /// </summary>
    private static FileSystemPath? ResolveAssemblyFile(IAssembly assembly)
    {
        FileSystemPath? assemblyFile = null;
        foreach (IAssemblyFile candidate in assembly.GetFiles())
        {
            FileSystemPath? candidatePath = candidate.Location.AssemblyPhysicalPath?.ToNativeFileSystemPath();
            if (candidatePath == null || candidatePath.IsEmpty || !candidatePath.ExistsFile) continue;
            bool isRef = candidatePath.FullPath.Contains("/ref/") || candidatePath.FullPath.Contains("\\ref\\") || candidatePath.FullPath.Contains(".ref/");
            if (assemblyFile == null) assemblyFile = candidatePath;
            if (!isRef) { assemblyFile = candidatePath; break; }
        }
        return assemblyFile;
    }

    /// <summary>
    /// Runs the SourceLink-then-ILSpy fetch on the background task executor and
    /// blocks the calling thread (with a 2-minute ceiling) until the task signals
    /// completion. Returns null when the wait times out so the caller can surface
    /// "decompile abandoned" rather than caching an empty result. Bundles the
    /// three outputs (text, fromSourceLink flag, typed SourceLink outcome) into
    /// <see cref="DecompileFetchOutcome"/> so DecompileToCacheItem doesn't have
    /// to thread four parallel locals through a closure.
    /// </summary>
    // Initial SourceLink outcome before any HTTP attempt — captures which
    // branch we'll take so the banner can explain *why* SourceLink didn't
    // contribute when it didn't (Disabled by setting / SkippedMode for non-C# /
    // NotAttempted while the fetch is still pending). Extracted out of the
    // FetchDecompiledContent prologue so the nested ternary doesn't obscure
    // the more interesting work that follows it.
    private static SourceLinkOutcome InitialSourceLinkOutcome(bool preferSourceLink, IlSpyOutputMode mode)
    {
        if (!preferSourceLink) return SourceLinkOutcome.Plain(SourceLinkStatus.Disabled);
        if (mode != IlSpyOutputMode.CSharp) return SourceLinkOutcome.Plain(SourceLinkStatus.SkippedMode);
        return SourceLinkOutcome.Plain(SourceLinkStatus.NotAttempted);
    }

    private DecompileFetchOutcome? FetchDecompiledContent(
        string assemblyPath,
        string typeFullName,
        string taskTitle,
        IlSpyRequestSettings request,
        ITaskExecutor taskExecutor)
    {
        SourceLinkOutcome initialOutcome = InitialSourceLinkOutcome(request.PreferSourceLink, request.Mode);
        // Single bundled result holder — the worker writes one immutable
        // DecompileFetchOutcome and we read it once after Wait. Replaces the
        // prior pattern of three parallel closure mutables (content,
        // fromSourceLink, sourceLinkOutcome) being mutated independently and
        // re-bundled at the bottom. Reduces the lambda's shared-state surface
        // to one nullable reference.
        DecompileFetchOutcome? result = null;
        // CTS is hoisted out of the lambda so the wait-side can cancel the
        // worker on timeout. Previously the cts was scoped to the lambda, so
        // when Wait(2 min) returned false the worker kept running and kept
        // mutating closure state we no longer cared about. Sharing the cts
        // gives the wait-side its one escape hatch to abort the in-flight
        // SourceLink HTTP fetch / DecompileType call.
        using CancellationTokenSource cts = new CancellationTokenSource();
        using ManualResetEventSlim doneSignal = new ManualResetEventSlim(false);
        taskExecutor.ExecuteTask(taskTitle, TaskCancelable.Yes, progress =>
        {
            // Bridge the platform's IProgressIndicator.IsCanceled poll into the
            // shared CancellationToken so FetchSourceLink's HTTP fetch is
            // actually cancellable via Rider's task cancel button (previously
            // the token parameter existed end-to-end but no caller ever passed
            // a live one).
            using Timer cancelPoll = new Timer(_ =>
            {
                if (progress.IsCanceled)
                {
                    try { cts.Cancel(); } catch (ObjectDisposedException) { /* benign race with disposal */ }
                }
            }, null, dueTime: 100, period: 100);
            try
            {
                SourceLinkOutcome outcome = initialOutcome;
                if (request.PreferSourceLink && request.Mode == IlSpyOutputMode.CSharp)
                {
                    SourceLinkAttempt attempt = mySourceLinkGateway.Fetch(assemblyPath, typeFullName, request.SourceLinkTimeoutSeconds, cts.Token);
                    outcome = attempt.Outcome;
                    if (attempt.Content != null)
                    {
                        result = new DecompileFetchOutcome(attempt.Content, FromSourceLink: true, outcome);
                        return;
                    }
                }
                string content = myDecompiler.DecompileType(assemblyPath, typeFullName, request.DecompilerSettings, request.ExtraSearchDirs, request.Mode).Content;
                result = new DecompileFetchOutcome(content, FromSourceLink: false, outcome);
            }
            finally
            {
                doneSignal.Set();
            }
        });
        if (!doneSignal.Wait(TimeSpan.FromMinutes(2)))
        {
            // Worker is still running — signal it to stop so it doesn't keep
            // burning CPU on a result we can't use. Caller gets null and the
            // navigation surface reports "decompile abandoned". Without this
            // the lambda would run to completion and silently mutate `result`
            // long after we've already returned null to the caller.
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* benign race with disposal */ }
            return null;
        }
        return result;
    }

    // Banner-metadata seam that adds Warn-on-null logging around the SDK-free
    // IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata helper.
    // Inlined here (rather than living on IlSpyDecompiler) because the warning
    // is a navigation-surface concern — the decompiler shouldn't grow a
    // logging-only shim for a helper that doesn't touch ICSharpCode.Decompiler.
    private AssemblyBannerMetadata? ReadBannerMetadata(string assemblyPath)
    {
        AssemblyBannerMetadata? result = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(assemblyPath);
        if (result == null && File.Exists(assemblyPath))
            ourLogger.Warn("RiderIlSpy.ReadBannerMetadata returned null for " + assemblyPath);
        return result;
    }

    /// <summary>
    /// Writes the decompiled <paramref name="content"/> into Rider's navigation
    /// cache, identified by the (assembly, moniker, fileName) triple.
    /// <see cref="IlSpyExternalSourcesProviderHelpers.BuildCacheProperties"/> returns
    /// a concrete <c>Dictionary&lt;,&gt;</c> so we pass it straight to
    /// PutCacheItem's IDictionary parameter — no cast or interface gymnastics needed.
    /// </summary>
    private DecompilationCacheItem? WriteToCache(IAssembly assembly, string assemblyPath, string typeFullName, string moniker, string fileName, IlSpyOutputMode mode, string content)
    {
        Dictionary<string, string> properties = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, assemblyPath, typeFullName, moniker, fileName);
        return myCache.PutCacheItem(Id, assembly, moniker, fileName, properties, content, sourceDebugData: null);
    }

    // TrackEntry / LRU cache moved to TypeEntryCache.cs — the provider holds an
    // instance via myEntryCache. Old fields (myEntries, myEntriesAccessLock,
    // myEntriesOrder, MaxTrackedTypes) are gone.

    // Provider-layer wrapper: delegates to the SDK-free
    // IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields parser
    // and attaches the IAssembly handle. Keeping the parse pure means the
    // property-bag round-trip is unit-testable without standing up a JetBrains
    // platform fixture.
    private static TypeDecompileEntry? TryParseEntry(IDictionary<string, string>? properties, IAssembly assembly)
    {
        DecompileEntryFields? fields = IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields(properties);
        if (fields == null) return null;
        return new TypeDecompileEntry(assembly, fields.AssemblyFilePath, fields.TypeFullName, fields.Moniker, fields.FileName, fields.Mode);
    }

    private IlSpyOutputMode? ReadRdMode()
    {
        string? current = myRiderIlSpyModel.Mode.Value;
        if (string.IsNullOrEmpty(current)) return null;
        // Wire strings are encoded as IlSpyOutputMode member names by the
        // kotlin frontend (see IlSpyMode.backendName). Single Enum.TryParse
        // covers all current modes and any future additions automatically.
        if (!Enum.TryParse(current, out IlSpyOutputMode mode)) return null;
        return mode;
    }

    // Canonical mode-resolution seam: prefer the live wire value when present,
    // fall back to the persisted setting otherwise. Documented once in
    // RiderIlSpyModel.kt and centralized here so DecompileToCacheItem and
    // RedecompileAllEntries agree on the policy by construction.
    private IlSpyOutputMode ResolveEffectiveMode()
        => ReadRdMode() ?? mySettings.GetValue((IlSpySettings s) => s.OutputMode);

    // Delegates the heavy lifting to IlSpyRequestSettingsBuilder, passing the
    // navigation-path's resolved mode (rd-live preferred over persisted).
    // Per-request snapshot guarantees the rest of the pass sees one consistent
    // view even if a settings write lands mid-flight.
    private IlSpyRequestSettings SnapshotRequestSettings() => mySettingsBuilder.Snapshot(ResolveEffectiveMode());

    // RD signals must fire on the protocol's Shell Rd Dispatcher, never on a
    // .NET thread-pool worker — firing from off-thread trips an assertion in
    // rd's FrontendBackend and the readyTick is dropped, so the status-bar
    // widget never sees the mode change complete. Queue() schedules the Fire
    // onto the protocol scheduler regardless of which thread we're on.
    // Kept on the provider (not the redecompiler) because it closes over
    // myRiderIlSpyModel — the redecompiler stays SDK-decoupled by taking this
    // as an Action delegate.
    private void FireReadyTickOnProtocolThread()
    {
        IProtocol? protocol = ((IRdDynamic)myRiderIlSpyModel).TryGetProto();
        if (protocol != null)
            protocol.Scheduler.Queue(() => myRiderIlSpyModel.ReadyTick.Fire(DateTime.UtcNow.Ticks));
    }

    private async Task RedecompileAllEntriesAsync(CancellationToken cancellationToken)
    {
        if (myEntryCache.IsEmpty) return;

        // One snapshot for the whole pass — concurrent settings writes can't
        // slice mode-vs-banner-vs-search-dirs across iterations now.
        IlSpyRequestSettings request = SnapshotRequestSettings();

        // Snapshot the cache too so a concurrent Track call during this pass
        // doesn't mutate the collection we're iterating. The redecompile is
        // long-running; cache writes from new navigations are common.
        foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntryCache.Snapshot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDecompileEntry entry = kv.Value;
            try
            {
                // Decompilation is pure CPU work — keep it on the worker thread.
                // The read lock is only needed for PutCacheItem below, which
                // touches Rider's project-model-backed cache.
                string content = myDecompiler.DecompileType(entry.AssemblyFilePath, entry.TypeFullName, request.DecompilerSettings, request.ExtraSearchDirs, request.Mode).Content;
                AssemblyBannerMetadata? bannerMeta = request.ShowBanner ? ReadBannerMetadata(entry.AssemblyFilePath) : null;
                // Redecompile path doesn't re-attempt SourceLink — the toggle is between
                // ILSpy output modes (C# / IL / Mixed). Use the no-outcome overload so
                // we don't synthesize a placeholder SourceLinkOutcome just to indicate
                // "we didn't try" — null already says that to the formatter.
                BannerContext bannerCtx = new BannerContext(bannerMeta, entry.AssemblyFilePath, entry.TypeFullName, request.Mode, request.ExtraSearchDirs);
                content = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(request.ShowBanner, bannerCtx, content);

                // Reuses the same WriteToCache helper as DecompileToCacheItem to
                // keep the decompile -> banner -> cache pipeline single-sourced.
                // PutCacheItem requires a reader lock (it walks the assembly's
                // project-model entry); StartReadActionAsync is the interruptible
                // path that respects WriteLock acquisition, so we wrap the
                // synchronous WriteToCache call in it. The foreach variable is
                // captured by the lambda — C# 5+ makes that capture per-iteration,
                // so no explicit alias is needed.
                await ReadActionUtil.StartReadActionAsync(
                    myShellLocks,
                    myLifetime,
                    () => WriteToCache(entry.Assembly, entry.AssemblyFilePath, entry.TypeFullName, entry.Moniker, entry.FileName, request.Mode, content)).ConfigureAwait(false);
                // TypeDecompileEntry is immutable — swap in a new entry with
                // the updated Mode via TypeEntryCache.Track, which holds the
                // cache lock so the swap is atomic with respect to the sync
                // sameMode check in DecompileToCacheItem.
                myEntryCache.Track(entry.Moniker, entry with { Mode = request.Mode });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ourLogger.Error(ex, "RiderIlSpy.RedecompileEntry failed for " + entry.TypeFullName);
            }
        }
    }

    // TypeDecompileEntry record moved to its own file (TypeDecompileEntry.cs)
    // so it can be referenced by TypeEntryCache without exposing internals of
    // this provider.

    private static ITypeElement? GetTopLevelTypeElement(ICompiledElement element)
    {
        ITypeElement? typeElement = element as ITypeElement;
        if (typeElement == null && element is ITypeMember member) typeElement = member.GetContainingType();
        while (typeElement != null && typeElement.GetContainingType() != null)
            typeElement = typeElement.GetContainingType();
        return typeElement;
    }
}
