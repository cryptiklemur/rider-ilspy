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
using JetBrains.ProjectModel.Model2.References;
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
    // Rider's built-in decompiler cache id (mirrors DecompiledSourcesConstants.Id
    // in the SDK). We deliberately write our cache items under this id rather
    // than a plugin-private one: the platform's built-in
    // DecompiledSourcesExternalSourcesProvider is the ONLY provider Rider's
    // debugger consults for decompiled-source debug data, and it serves
    // SourceDebugData only for items tagged with this id (and stored under its
    // cache subfolder). Sharing the namespace is what lets breakpoints bind in
    // decompiled BCL/NuGet source against our ILSpy-generated sequence points.
    // The cost — Rider's dotPeek can serve a stale ILSpy-authored entry after
    // ILSpy is toggled off — is handled by EvictTrackedEntries on disable.
    private const string DecompilerId = "decompiler";

    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyExternalSourcesProvider>();

    private readonly INavigationDecompilationCache myCache;
    private readonly IIlSpyEngine myEngine;
    private readonly IlSpySourceLinkGateway mySourceLinkGateway;
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly IShellLocks myShellLocks;
    private readonly Lifetime myLifetime;
    private readonly IModuleReferencesResolveStore myReferencesResolveStore;
    private readonly TypeEntryCache myEntryCache = new TypeEntryCache();
    private readonly IlSpyRequestSettingsBuilder mySettingsBuilder;
    private readonly RiderIlSpyModel myRiderIlSpyModel;
    private readonly ModeChangeRedecompiler myModeChangeRedecompiler;
    // Mirrors the frontend's master on/off flag, pushed from
    // IlSpyProtocolHost via the rd `Enabled` property. Default true so the
    // window between solution open and the first rd push matches the legacy
    // "enabled" default rather than silently disabling the provider on
    // every solution open. Volatile because IsIlSpyEnabled is called from
    // navigation threads while Advise runs on the protocol scheduler.
    private volatile bool myEnabled = true;

    public IlSpyExternalSourcesProvider(
        Lifetime lifetime,
        ISolution solution,
        ISettingsStore settingsStore,
        INavigationDecompilationCache cache,
        IlSpyEngineHost engineHost,
        IlSpySourceLinkGateway sourceLinkGateway,
        IShellLocks shellLocks,
        IModuleReferencesResolveStore referencesResolveStore)
    {
        myCache = cache;
        myEngine = engineHost.Engine;
        mySourceLinkGateway = sourceLinkGateway;
        myShellLocks = shellLocks;
        myLifetime = lifetime;
        myReferencesResolveStore = referencesResolveStore;
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
        // Track the frontend's enabled flag so IsApplicableForNavigation /
        // IsPreferredForNavigation can short-circuit the entire provider
        // when the user has toggled ILSpy off from the status bar widget.
        // On an enable->disable transition we also evict everything we wrote
        // under the shared "decompiler" cache id (see OnEnabledChanged) so
        // Rider's own dotPeek re-decompiles fresh instead of serving our
        // stale ILSpy-authored entry.
        myRiderIlSpyModel.Enabled.Advise(lifetime, OnEnabledChanged);
    }

    public string PresentableShortName => "ILSpy";
    public string Id => DecompilerId;
    public int Priority => 100;

    public bool IsApplicableForNavigation(CompiledElementNavigationInfo? navigationInfo, bool ignoreOptions)
    {
        // Pure gating decision (enabled / ignoreOptions) lives in a helper so
        // it can be unit-tested without loading the IExternalSourcesProvider
        // type graph at test runtime.
        return IlSpyNavigationApplicability.Decide(IsIlSpyEnabled(), ignoreOptions);
    }

    public bool IsPreferredForNavigation()
    {
        // IlSpyNavigationPreference.Decide is a pure helper kept outside this
        // class so the logic is unit-testable without the IExternalSourcesProvider
        // type graph. Returns true whenever the plugin is enabled, which makes
        // ILSpy the *preferred* provider — the orchestrator
        // (ExternalSourcesServiceImpl.NavigateToSources) puts the preferred
        // provider at the front of its iteration and only falls through to
        // peer providers when our NavigateToSources returns empty. Without
        // this, JB's bundled DecompiledSourcesExternalSourcesProvider
        // (registered before our plugin) wins every navigation race even
        // when ILSpy is enabled.
        return IlSpyNavigationPreference.Decide(IsIlSpyEnabled());
    }

    // Source of truth for the master on/off flag is the Kotlin frontend's
    // IlSpyFrontendSettings, mirrored into myEnabled via the rd Enabled
    // property. Reading the field (instead of mySettings.GetValue) lets the
    // status-bar widget's "Off" toggle take effect immediately on the next
    // navigation without a settings-store write round-trip. The window
    // between solution open and the first rd push is covered by the field's
    // default-true initializer.
    private bool IsIlSpyEnabled() => myEnabled;

    // rd Enabled-property adviser. Records the new flag, and on an
    // enable->disable transition evicts every cache entry we authored so
    // Rider's own dotPeek decompiler re-runs on the next navigation instead
    // of reusing our ILSpy output (banner + synthetic debug data) from the
    // shared "decompiler" cache namespace. The transition predicate is the
    // pure IlSpyExternalSourcesProviderHelpers.ShouldEvictOnEnabledChange so
    // the "evict only on true->false" rule is unit-tested without the SDK.
    // The initial advise-fire (value == default true) is a no-op transition.
    private void OnEnabledChanged(bool enabled)
    {
        bool shouldEvict = IlSpyExternalSourcesProviderHelpers.ShouldEvictOnEnabledChange(myEnabled, enabled);
        myEnabled = enabled;
        if (shouldEvict) EvictTrackedEntries();
    }

    // Deletes the on-disk cache files (content + ".p" properties + ".dd" debug
    // data sidecars) for every entry we've tracked, computing each path via the
    // same GetFilePath hash PutCacheItem uses. The platform cache exposes no
    // per-item removal — only the nuclear ClearCache() — so targeted file
    // deletion is the way to drop just our entries and leave any unrelated
    // dotPeek cache items intact. After deletion, GetCacheItem misses for those
    // files and dotPeek re-decompiles fresh. The tracked entries are left in
    // myEntryCache: re-enabling ILSpy and re-navigating re-populates them on a
    // cache miss, and RedecompileAllEntriesAsync is guarded to no-op while
    // disabled so a stray mode change can't resurrect the evicted ILSpy output.
    private void EvictTrackedEntries()
    {
        foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntryCache.Snapshot())
        {
            TypeDecompileEntry entry = kv.Value;
            try
            {
                FileSystemPath path = myCache.GetFilePath(Id, entry.Assembly, entry.Moniker, entry.FileName);
                DeleteIfExists(path);
                DeleteIfExists(path.AddSuffix(".p"));
                DeleteIfExists(path.AddSuffix(".dd"));
            }
            catch (Exception ex)
            {
                ourLogger.Error(ex, "RiderIlSpy.EvictTrackedEntries failed for " + entry.TypeFullName);
            }
        }
    }

    private static void DeleteIfExists(FileSystemPath path)
    {
        if (path.ExistsFile) path.DeleteFile();
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

    // GetTypeDebugData is the type-keyed entry the platform uses for IDE
    // inspection features that haven't navigated to a source file yet. We don't
    // pre-decompile types speculatively, so we have nothing to hand back at that
    // entry point — the actual breakpoint-binding flow comes through the
    // file-keyed GetSourceDebugData once the user opens decompiled source, and
    // that path *is* implemented below.
    public ExtendedDebugData? GetTypeDebugData(ICompiledElement type, ITaskExecutor taskExecutor) => null;

    /// <summary>
    /// Surface the per-method sequence-point graph the debugger needs to bind
    /// breakpoints in decompiled source for a file we cached. Looks up the
    /// cache entry by file path, pulls the previously-written <c>DebugData</c>
    /// (built by <see cref="DebugDataFactory.BuildDebugData"/> at PutCacheItem
    /// time), and wraps it as <see cref="ExtendedDebugData"/> tagged
    /// <see cref="DebugDataOrigin.Decompiled"/> so Rider knows the SPs come
    /// from decompiled source rather than a real PDB.
    /// </summary>
    public ExtendedDebugData? GetSourceDebugData(FileSystemPath file)
    {
        if (!myCache.CanBeCachedFile(Id, file)) return null;
        DecompilationCacheItem? item = myCache.GetCacheItem(file);
        DebugData? debugData = item?.SourceDebugData;
        if (debugData == null) return null;
        // Source the assembly path from the cached property bag rather than
        // IAssembly.Location.ContainerPhysicalPath — that property is typed
        // VirtualFileSystemPath in this SDK and the ExtendedDebugData.Create
        // overload we use wants a string. TryParseEntry already round-trips
        // the property bag for the navigation path, so reuse it here.
        TypeDecompileEntry? entry = TryParseEntry(item!.Properties, item.Assembly);
        if (entry == null) return null;
        return DebugDataFactory.WrapForAssembly(debugData, item.Assembly.Id, entry.AssemblyFilePath, item.Assembly.FullAssemblyName);
    }

    // Claim ownership of files we cached so the platform's debug-data orchestrator
    // routes the per-file query to us instead of polling siblings — none of them
    // have written SPs for our cache entries. Returning true here implies
    // GetSourceDebugData must yield real data; the lookup matches the same
    // (decompilerId, file) test PutCacheItem keyed the entry under.
    public bool IsPreferredForGettingDebugData(FileSystemPath file) => myCache.CanBeCachedFile(Id, file);

    private DecompilationCacheItem? DecompileToCacheItem(ICompiledElement compiledElement, ITaskExecutor taskExecutor)
    {
        try
        {
            ITypeElement? top = GetTopLevelTypeElement(compiledElement);
            if (top == null) return null;

            IAssembly? assembly = top.Module.ContainingProjectModule as IAssembly;
            if (assembly == null) return null;

            // Snapshot request settings before resolve so the resolve pass can
            // honor the DecompileReferenceAssemblies gate (refuse to emit
            // ILSpy ref-stub output and let Rider's built-in decompiler take
            // the navigation instead) under the same view of settings the
            // rest of this pass uses.
            IlSpyRequestSettings request = SnapshotRequestSettings();

            FileSystemPath? assemblyFile = ResolveAssemblyFile(assembly, request.DecompileReferenceAssemblies);
            if (assemblyFile == null) return null;
            ourLogger.Verbose("RiderIlSpy.ResolveAssemblyFile picked " + assemblyFile.FullPath + " for " + assembly.FullAssemblyName);

            IClrTypeName? clrName = top.GetClrName();
            if (clrName == null) return null;
            string fullName = clrName.FullName;
            if (string.IsNullOrEmpty(fullName)) return null;
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
    /// method bodies — useless for source navigation.
    ///
    /// Resolution proceeds in two passes:
    /// 1) Walk the csproj <c>&lt;Reference&gt;</c> items via
    ///    <c>IModuleReferencesResolveStore.GetReferencesToAssemblyForAllContexts</c>
    ///    and pick the best HintPath. This surfaces every project-level reference
    ///    (including wildcard <c>Include="*.dll"</c> glob expansions) so when the
    ///    user's csproj points at both a publicised ref pack (e.g. Krafs.RimWorld.Ref)
    ///    AND a local impl directory (RimWorldLinux_Data/Managed/), we prefer the
    ///    impl and decompile full method bodies instead of stubs.
    /// 2) Fall back to <c>IAssembly.GetFiles()</c> for assemblies that don't have
    ///    project-level references (BCL types, NuGet runtime refs, transitively-pulled
    ///    assemblies).
    /// </summary>
    private FileSystemPath? ResolveAssemblyFile(IAssembly assembly, bool allowRefAssemblies)
    {
        FileSystemPath? fromCsprojWildcards = TryResolveFromCsprojWildcards(assembly, allowRefAssemblies);
        if (fromCsprojWildcards != null) return fromCsprojWildcards;
        FileSystemPath? fromProjectRefs = TryResolveFromProjectReferences(assembly, allowRefAssemblies);
        if (fromProjectRefs != null) return fromProjectRefs;
        return PickFromAssemblyFiles(assembly, allowRefAssemblies);
    }

    /// <summary>
    /// Plan-B resolution path: bypass Rider's MSBuild-resolved reference graph
    /// (which dedups to a single reference per assembly identity, dropping the
    /// wildcard <c>Include="path/*.dll"</c> impl candidates a project's csproj
    /// explicitly lists) and walk the raw csproj XML of every project that
    /// references <paramref name="assembly"/>. Returns the impl-preferred match
    /// or null when no csproj reference yields an on-disk file matching the
    /// assembly's short name.
    /// </summary>
    private FileSystemPath? TryResolveFromCsprojWildcards(IAssembly assembly, bool allowRefAssemblies)
    {
        try
        {
            string? assemblyShortName = IlSpyExternalSourcesProviderHelpers.TryParseAssemblyShortName(assembly.FullAssemblyName);
            if (string.IsNullOrEmpty(assemblyShortName)) return null;

            ICollection<IModuleToAssemblyReference> refs = myReferencesResolveStore.GetReferencesToAssemblyForAllContexts(assembly);
            if (refs.Count == 0) return null;

            HashSet<string> projectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IModuleToAssemblyReference r in refs)
            {
                IProjectToAssemblyReference? projectRef = r as IProjectToAssemblyReference;
                if (projectRef == null) continue;
                IProject project = projectRef.OwnerModule;
                FileSystemPath nativePath = project.ProjectFileLocation.ToNativeFileSystemPath();
                if (nativePath.IsEmpty) continue;
                projectFiles.Add(nativePath.FullPath);
            }

            if (projectFiles.Count == 0) return null;

            List<string> candidates = new List<string>();
            foreach (string projectFile in projectFiles)
            {
                IReadOnlyList<string> matches = IlSpyExternalSourcesProviderHelpers.EnumerateCsprojReferenceCandidates(
                    projectFile,
                    assemblyShortName!,
                    File.ReadAllText,
                    Directory.EnumerateFiles);
                foreach (string m in matches) candidates.Add(m);
            }

            string? picked = IlSpyExternalSourcesProviderHelpers.PickImplPathFromCandidates(candidates, File.Exists, allowRefAssemblies);
            return picked == null ? null : FileSystemPath.Parse(picked);
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.TryResolveFromCsprojWildcards failed for " + assembly.FullAssemblyName);
            return null;
        }
    }

    /// <summary>
    /// Reads every csproj <c>&lt;Reference&gt;</c> HintPath that targets
    /// <paramref name="assembly"/> (across all referencing projects + TFMs) and
    /// picks the best impl-preferred candidate. Returns null when no project-level
    /// reference exists or no HintPath points at an on-disk file.
    /// </summary>
    private FileSystemPath? TryResolveFromProjectReferences(IAssembly assembly, bool allowRefAssemblies)
    {
        try
        {
            ICollection<IModuleToAssemblyReference> refs = myReferencesResolveStore.GetReferencesToAssemblyForAllContexts(assembly);
            if (refs.Count == 0) return null;
            List<string?> hintPaths = new List<string?>(refs.Count);
            foreach (IModuleToAssemblyReference r in refs)
            {
                VirtualFileSystemPath hint = r.ReferenceTarget.HintLocation;
                if (hint.IsEmpty) continue;
                FileSystemPath nativeHint = hint.ToNativeFileSystemPath();
                if (nativeHint.IsEmpty) continue;
                hintPaths.Add(nativeHint.FullPath);
            }
            string? picked = IlSpyExternalSourcesProviderHelpers.PickImplPathFromCandidates(hintPaths, File.Exists, allowRefAssemblies);
            return picked == null ? null : FileSystemPath.Parse(picked);
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.TryResolveFromProjectReferences failed for " + assembly.FullAssemblyName);
            return null;
        }
    }

    private static FileSystemPath? PickFromAssemblyFiles(IAssembly assembly, bool allowRefAssemblies)
    {
        FileSystemPath? assemblyFile = null;
        foreach (IAssemblyFile candidate in assembly.GetFiles())
        {
            FileSystemPath? candidatePath = candidate.Location.AssemblyPhysicalPath?.ToNativeFileSystemPath();
            if (candidatePath == null || candidatePath.IsEmpty || !candidatePath.ExistsFile) continue;
            bool isRef = IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(candidatePath.FullPath);
            if (isRef && !allowRefAssemblies) continue;
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
                return new DecompileFetchOutcome(attempt.Content, FromSourceLink: true, outcome, Success: true, FailureReason: null, Methods: DecompileFetchOutcome.EmptyMethods);
            }
        }
        DecompileResult decompile = myEngine.DecompileType(assemblyPath, typeFullName, request.DecompilerOptions, request.ExtraSearchDirs, request.Mode);
        return new DecompileFetchOutcome(decompile.Content, FromSourceLink: false, outcome, decompile.Success, decompile.FailureReason, decompile.Methods);
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

    // Banner-metadata seam that adds Warn-on-null logging around the engine's
    // metadata reader. The warning is a navigation-surface concern — the engine
    // shouldn't grow a logging-only shim (and it has no JetBrains logger anyway).
    private AssemblyBannerMetadata? ReadBannerMetadata(string assemblyPath)
    {
        AssemblyBannerMetadata? result = myEngine.ReadAssemblyBannerMetadata(assemblyPath);
        if (result == null && File.Exists(assemblyPath))
            ourLogger.Warn("RiderIlSpy.ReadBannerMetadata returned null for " + assemblyPath);
        return result;
    }

    // Single seam used by both DecompileToCacheItem and RedecompileAllEntriesAsync
    // to build the BannerContext and prepend the banner. The two callers differ
    // only in whether they thread a SourceLinkOutcome (fetch path) or not
    // (redecompile path, where no SourceLink fetch is re-attempted) — so
    // sourceLinkOutcome is nullable. Early-returns when ShowBanner is off so we
    // skip the metadata read (matches the previous ternary). Returns the banner
    // line count alongside the prepended content so the debug-data pipeline can
    // shift ICSharpCode.Decompiler's un-bannered sequence-point line numbers
    // onto the lines the user actually sees in Rider — without that delta,
    // breakpoints would bind to the wrong source line (offset by banner height).
    private BannerApplication ApplyBannerIfEnabled(string assemblyPath, string typeFullName, IlSpyRequestSettings request, SourceLinkOutcome? sourceLinkOutcome, string content)
    {
        if (!request.ShowBanner) return new BannerApplication(content, BannerLineCount: 0);
        AssemblyBannerMetadata? bannerMeta = ReadBannerMetadata(assemblyPath);
        BannerContext bannerCtx = new BannerContext(bannerMeta, assemblyPath, typeFullName, request.Mode, request.ExtraSearchDirs, myEngine.GetDecompilerVersion());
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(bannerCtx, sourceLinkOutcome);
        return new BannerApplication(banner + content, IlSpyExternalSourcesProviderHelpers.CountBannerLines(banner));
    }

    private readonly record struct BannerApplication(string Content, int BannerLineCount);

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
        int bannerLineCount = 0;
        if (!fetch.FromSourceLink)
        {
            BannerApplication applied = ApplyBannerIfEnabled(assemblyFile.FullPath, fullName, request, fetch.SourceLinkOutcome, content);
            content = applied.Content;
            bannerLineCount = applied.BannerLineCount;
        }
        // SourceLink output (real upstream source) carries its own real PDB data
        // via the platform's normal pipeline, so we don't synthesize debug data
        // for it — only the decompiled path needs the synthetic sequence points.
        DebugData? sourceDebugData = fetch.FromSourceLink
            ? null
            : DebugDataFactory.BuildDebugData(GetCacheDocumentUrl(assembly, moniker, fileName), fetch.Methods, bannerLineCount);
        DecompilationCacheItem? result = WriteToCache(assembly, assemblyFile.FullPath, fullName, moniker, fileName, request.Mode, content, sourceDebugData);
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
    private DecompilationCacheItem? WriteToCache(IAssembly assembly, string assemblyPath, string typeFullName, string moniker, string fileName, IlSpyOutputMode mode, string content, DebugData? sourceDebugData)
    {
        Dictionary<string, string> properties = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, assemblyPath, typeFullName, moniker, fileName);
        return myCache.PutCacheItem(Id, assembly, moniker, fileName, properties, content, sourceDebugData);
    }

    // The debug-data document URL must be the *full cache file path* the entry
    // will be written to, not the bare file name. The debugger binds a
    // breakpoint by matching the breakpoint's document path (the absolute path
    // of the open decompiled file) against the document URL embedded in the
    // DebugData's sequence points — Rider's own dotPeek provider stores the
    // absolute cache path here, and a bare "Type.cs" never matches, so the
    // lookup returns no method and the breakpoint stays unbound. GetFilePath is
    // the same path computation PutCacheItem uses internally (a pure hash over
    // the assembly id + moniker, no PSI access), so calling it ahead of the
    // write yields exactly the path the content lands at.
    private string GetCacheDocumentUrl(IAssembly assembly, string moniker, string fileName)
        => myCache.GetFilePath(Id, assembly, moniker, fileName).FullPath;

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
        // A mode change while ILSpy is disabled must not rewrite cache entries:
        // we share the "decompiler" id with dotPeek, so resurrecting ILSpy
        // output here would undo the eviction done on the enable->disable
        // transition and serve stale banner/debug-data under dotPeek's name.
        if (!myEnabled) return;
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
                DecompileResult decompile = myEngine.DecompileType(entry.AssemblyFilePath, entry.TypeFullName, request.DecompilerOptions, request.ExtraSearchDirs, request.Mode);
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
                // Redecompile path doesn't re-attempt SourceLink — the toggle is between
                // ILSpy output modes (C# / IL / Mixed). Pass null sourceLinkOutcome so
                // we don't synthesize a placeholder just to indicate "we didn't try" —
                // null already says that to the formatter.
                BannerApplication applied = ApplyBannerIfEnabled(entry.AssemblyFilePath, entry.TypeFullName, request, sourceLinkOutcome: null, decompile.Content);
                string content = applied.Content;
                DebugData? sourceDebugData = DebugDataFactory.BuildDebugData(GetCacheDocumentUrl(entry.Assembly, entry.Moniker, entry.FileName), decompile.Methods, applied.BannerLineCount);

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
                    () => WriteToCache(entry.Assembly, entry.AssemblyFilePath, entry.TypeFullName, entry.Moniker, entry.FileName, request.Mode, content, sourceDebugData)).ConfigureAwait(false);
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
