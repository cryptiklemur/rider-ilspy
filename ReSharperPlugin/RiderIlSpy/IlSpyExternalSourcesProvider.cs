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
    }

    public string PresentableShortName => "ILSpy";
    public string Id => DecompilerId;
    public int Priority => 100;

    public bool IsApplicableForNavigation(CompiledElementNavigationInfo? navigationInfo, bool ignoreOptions)
    {
        // navigationInfo is uniformly ignored: the provider applies to any
        // compiled element regardless of which sub-kind the platform is
        // navigating to. The ignoreOptions branching lives in the pure
        // static helper IlSpyNavigationApplicability.Decide so it can be
        // unit-tested without loading the IExternalSourcesProvider type
        // graph at test runtime.
        return IlSpyNavigationApplicability.Decide(IsIlSpyEnabled(), ignoreOptions);
    }

    public bool IsPreferredForNavigation()
    {
        // IlSpyNavigationPreference.Decide is a pure helper kept outside this
        // class so the logic is unit-testable without the IExternalSourcesProvider
        // type graph. Returning false (when DeferToRiderSources is on) lets
        // Rider's own preferred-source providers — downloaded source, SourceLink,
        // Microsoft Reference Source — win for types that ship real source.
        // ILSpy remains the fallback via IsApplicableForNavigation.
        return IlSpyNavigationPreference.Decide(
            IsIlSpyEnabled(),
            mySettings.GetValue((IlSpySettings s) => s.DeferToRiderSources));
    }

    private bool IsIlSpyEnabled()
    {
        return mySettings.GetValue((IlSpySettings s) => s.Enabled);
    }

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

            // Skip caching pure-failure output so a transient ICSharpCode.Decompiler
            // bug doesn't pin a comment-block file in Rider's external-sources
            // cache (and force the user to clear it manually to retry). The
            // failure trace is still surfaced via the logger for diagnosis.
            if (!fetch.Success)
            {
                ourLogger.Warn("RiderIlSpy.DecompileToCacheItem skipping cache write for " + fullName + ": " + (fetch.FailureReason ?? "unknown failure"));
                return null;
            }

            return WriteEnrichedCacheItem(assembly, assemblyFile, fullName, moniker, fileName, request, fetch);
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
            bool isRef = IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(candidatePath.FullPath);
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
    // The InitialSourceLinkOutcome choice (pure ternary over preferSourceLink +
    // mode) moved to IlSpyExternalSourcesProviderHelpers so it can be regression-
    // tested without dragging in the provider's SDK surface. The caller in
    // FetchDecompiledContent now delegates to that helper directly.

    private DecompileFetchOutcome? FetchDecompiledContent(
        string assemblyPath,
        string typeFullName,
        string taskTitle,
        IlSpyRequestSettings request,
        ITaskExecutor taskExecutor)
    {
        SourceLinkOutcome initialOutcome = IlSpyExternalSourcesProviderHelpers.InitialSourceLinkOutcome(request.PreferSourceLink, request.Mode);
        // The orchestration mechanics (task executor lambda, progress-cancel
        // poll, doneSignal wait, timeout cancel) live in RunFetchOnExecutor.
        // This method now just owns the SourceLink-vs-ILSpy choice and the
        // typed-outcome bundling.
        return RunFetchOnExecutor(
            taskExecutor,
            taskTitle,
            TimeSpan.FromMinutes(2),
            cancellationToken => FetchOnce(assemblyPath, typeFullName, request, initialOutcome, cancellationToken));
    }

    private DecompileFetchOutcome FetchOnce(
        string assemblyPath,
        string typeFullName,
        IlSpyRequestSettings request,
        SourceLinkOutcome initialOutcome,
        CancellationToken cancellationToken)
    {
        SourceLinkOutcome outcome = initialOutcome;
        if (request.PreferSourceLink && request.Mode == IlSpyOutputMode.CSharp)
        {
            SourceLinkAttempt attempt = mySourceLinkGateway.Fetch(assemblyPath, typeFullName, request.SourceLinkTimeoutSeconds, cancellationToken);
            outcome = attempt.Outcome;
            if (attempt.Content != null)
            {
                // SourceLink HTTP fetch only returns Content on success;
                // there's no failure-content path from the gateway, so
                // mirror that as Success=true.
                return new DecompileFetchOutcome(attempt.Content, FromSourceLink: true, outcome, Success: true, FailureReason: null);
            }
        }
        DecompileResult decompile = myDecompiler.DecompileType(assemblyPath, typeFullName, request.DecompilerSettings, request.ExtraSearchDirs, request.Mode);
        return new DecompileFetchOutcome(decompile.Content, FromSourceLink: false, outcome, decompile.Success, decompile.FailureReason);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on the Rider task executor's worker thread
    /// and blocks the calling thread (with a <paramref name="timeout"/>
    /// ceiling) until the worker signals completion. Bridges
    /// <see cref="IProgressIndicator.IsCanceled"/> into a shared
    /// <see cref="CancellationToken"/> on a 100ms poll so the body's HTTP /
    /// CPU work is cancellable via Rider's task-cancel button. Returns null
    /// when the wait times out — the body is signalled to stop so it doesn't
    /// keep burning CPU on a result no one is waiting for.
    /// </summary>
    private static T? RunFetchOnExecutor<T>(
        ITaskExecutor taskExecutor,
        string taskTitle,
        TimeSpan timeout,
        Func<CancellationToken, T> body) where T : class
    {
        // Single bundled result holder — the worker writes once and we read
        // once after Wait. Reduces the lambda's shared-state surface to one
        // nullable reference.
        T? result = null;
        // CTS is hoisted out of the lambda so the wait-side can cancel the
        // worker on timeout. Without this the lambda would run to completion
        // and silently mutate `result` long after the caller stopped caring.
        using CancellationTokenSource cts = new CancellationTokenSource();
        using ManualResetEventSlim doneSignal = new ManualResetEventSlim(false);
        taskExecutor.ExecuteTask(taskTitle, TaskCancelable.Yes, progress =>
        {
            using Timer cancelPoll = new Timer(_ =>
            {
                if (progress.IsCanceled)
                {
                    try { cts.Cancel(); } catch (ObjectDisposedException) { /* benign race with disposal */ }
                }
            }, null, dueTime: 100, period: 100);
            try
            {
                result = body(cts.Token);
            }
            finally
            {
                doneSignal.Set();
            }
        });
        if (!doneSignal.Wait(timeout))
        {
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

    // Single seam used by both DecompileToCacheItem and RedecompileAllEntriesAsync
    // to build the BannerContext and prepend the banner. The two callers differ
    // only in whether they thread a SourceLinkOutcome (fetch path) or not
    // (redecompile path, where no SourceLink fetch is re-attempted) — so
    // sourceLinkOutcome is nullable. Early-returns when ShowBanner is off so we
    // skip the metadata read (matches the previous ternary).
    private string ApplyBannerIfEnabled(string assemblyPath, string typeFullName, IlSpyRequestSettings request, SourceLinkOutcome? sourceLinkOutcome, string content)
    {
        if (!request.ShowBanner) return content;
        AssemblyBannerMetadata? bannerMeta = ReadBannerMetadata(assemblyPath);
        BannerContext bannerCtx = new BannerContext(bannerMeta, assemblyPath, typeFullName, request.Mode, request.ExtraSearchDirs);
        return IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(showBanner: true, bannerCtx, sourceLinkOutcome, content);
    }

    // Post-fetch commit phase: banner enrichment (skipped for SourceLink output
    // — see ApplyBannerIfEnabled rationale), WriteToCache, then entry tracking.
    // Extracted out of DecompileToCacheItem so that method reads as a top-down
    // pipeline (resolve → snapshot → fetch → finalize) instead of mixing the
    // four phases in one body. The SourceLink-vs-ILSpy banner gating stays
    // here because it's part of the finalize step's contract.
    private DecompilationCacheItem? WriteEnrichedCacheItem(
        IAssembly assembly,
        FileSystemPath assemblyFile,
        string fullName,
        string moniker,
        string fileName,
        IlSpyRequestSettings request,
        DecompileFetchOutcome fetch)
    {
        string content = fetch.Content;
        if (!fetch.FromSourceLink)
        {
            content = ApplyBannerIfEnabled(assemblyFile.FullPath, fullName, request, fetch.SourceLinkOutcome, content);
        }
        DecompilationCacheItem? result = WriteToCache(assembly, assemblyFile.FullPath, fullName, moniker, fileName, request.Mode, content);
        if (result != null)
        {
            myEntryCache.Track(moniker, new TypeDecompileEntry(assembly, assemblyFile.FullPath, fullName, moniker, fileName, request.Mode));
        }
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
                DecompileResult decompile = myDecompiler.DecompileType(entry.AssemblyFilePath, entry.TypeFullName, request.DecompilerSettings, request.ExtraSearchDirs, request.Mode);
                if (!decompile.Success)
                {
                    // Skip overwriting the existing cache entry with a
                    // failure trace — if the user had a working decompile
                    // before the redecompile pass, preserving it is better
                    // than replacing it with a comment block for a transient
                    // ICSharpCode.Decompiler failure.
                    ourLogger.Warn("RiderIlSpy.RedecompileEntry skipping cache replace for " + entry.TypeFullName + ": " + (decompile.FailureReason ?? "unknown failure"));
                    continue;
                }
                string content = decompile.Content;
                // Redecompile path doesn't re-attempt SourceLink — the toggle is between
                // ILSpy output modes (C# / IL / Mixed). Pass null sourceLinkOutcome so
                // we don't synthesize a placeholder just to indicate "we didn't try" —
                // null already says that to the formatter.
                content = ApplyBannerIfEnabled(entry.AssemblyFilePath, entry.TypeFullName, request, sourceLinkOutcome: null, content);

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


    private static ITypeElement? GetTopLevelTypeElement(ICompiledElement element)
    {
        ITypeElement? typeElement = element as ITypeElement;
        if (typeElement == null && element is ITypeMember member) typeElement = member.GetContainingType();
        while (typeElement != null && typeElement.GetContainingType() != null)
            typeElement = typeElement.GetContainingType();
        return typeElement;
    }
}
