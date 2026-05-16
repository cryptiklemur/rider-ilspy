using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using JetBrains.Application.Parts;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Debug;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.Rd.Tasks;
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
    private const string DecompilerIdConst = "RiderIlSpy";
    private const int MaxTrackedTypes = 512;

    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyExternalSourcesProvider>();

    private readonly INavigationDecompilationCache myCache;
    private readonly IlSpyDecompiler myDecompiler;
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly ConcurrentDictionary<string, TypeDecompileEntry> myEntries = new ConcurrentDictionary<string, TypeDecompileEntry>();
    private readonly object myEntriesAccessLock = new object();
    private readonly LinkedList<string> myEntriesOrder = new LinkedList<string>();
    private readonly RiderIlSpyModel myRiderIlSpyModel;
    private CancellationTokenSource? myActiveRedecompileCts;
    private readonly object myRedecompileLock = new object();

    public IlSpyExternalSourcesProvider(Lifetime lifetime, ISolution solution, ISettingsStore settingsStore, INavigationDecompilationCache cache, IlSpyDecompiler decompiler)
    {
        myCache = cache;
        myDecompiler = decompiler;
        mySettings = settingsStore.BindToContextLive(lifetime, ContextRange.ApplicationWide);
        myRiderIlSpyModel = solution.GetProtocolSolution().GetRiderIlSpyModel();
        myRiderIlSpyModel.Mode.Advise(lifetime, OnRiderIlSpyModeChanged);
        // Endpoint for "Save Decompiled Assembly as Project..." — frontend kotlin
        // action calls this via the rd protocol. SetSync runs the handler on the
        // protocol thread; the kotlin side wraps the call in a backgroundable
        // progress task so the IDE stays responsive while ILSpy churns.
        myRiderIlSpyModel.SaveAsProject.SetSync(OnSaveAsProjectRequest);
    }

    /// <summary>
    /// rd handler for <see cref="RiderIlSpyModel"/>.SaveAsProject. Wraps
    /// <see cref="IlSpyDecompiler.DecompileAssemblyToProject"/> and reports either
    /// a successful summary or an error message back across the protocol. Never
    /// throws to the rd layer — exceptions become <c>success=false</c> responses
    /// so the kotlin action can render them as a notification.
    /// </summary>
    private SaveAsProjectResponse OnSaveAsProjectRequest(Lifetime _, SaveAsProjectRequest request)
    {
        try
        {
            DecompilerSettings settings = BuildDecompilerSettings();
            IReadOnlyList<string> extraDirs = GetExtraSearchDirs();
            DecompileAssemblyToProjectResult result = myDecompiler.DecompileAssemblyToProject(
                request.AssemblyPath,
                request.TargetDirectory,
                settings,
                extraDirs,
                CancellationToken.None);
            return new SaveAsProjectResponse(
                success: true,
                projectFilePath: result.ProjectFilePath ?? "",
                csharpFileCount: result.CSharpFileCount,
                errorMessage: "");
        }
        catch (System.Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.SaveAsProject failed for " + request.AssemblyPath);
            return new SaveAsProjectResponse(
                success: false,
                projectFilePath: "",
                csharpFileCount: 0,
                errorMessage: ex.Message ?? ex.GetType().Name);
        }
    }

    public string PresentableShortName => "ILSpy";
    public string Id => DecompilerIdConst;
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

    private DecompilerSettings BuildDecompilerSettings()
    {
        DecompilerSettings settings = new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = mySettings.GetValue((IlSpySettings s) => s.ThrowOnAssemblyResolveErrors),
            AsyncAwait = mySettings.GetValue((IlSpySettings s) => s.AsyncAwait),
            UseExpressionBodyForCalculatedGetterOnlyProperties = mySettings.GetValue((IlSpySettings s) => s.ExpressionBodies),
            NamedArguments = mySettings.GetValue((IlSpySettings s) => s.NamedArguments),
            ShowXmlDocumentation = mySettings.GetValue((IlSpySettings s) => s.ShowXmlDocumentation),
            RemoveDeadCode = mySettings.GetValue((IlSpySettings s) => s.RemoveDeadCode),
            UsePrimaryConstructorSyntax = mySettings.GetValue((IlSpySettings s) => s.UsePrimaryConstructorSyntax),
        };
        // Apply language-version downgrade after construction so unspecified
        // (Latest) leaves ILSpy's defaults untouched. SetLanguageVersion
        // flips multiple feature flags (RecordClasses, InitAccessors, ...)
        // to match the target version's capability set.
        IlSpyLanguageVersion languageVersion = mySettings.GetValue((IlSpySettings s) => s.LanguageVersion);
        if (languageVersion != IlSpyLanguageVersion.Latest)
        {
            settings.SetLanguageVersion((LanguageVersion)(int)languageVersion);
        }
        return settings;
    }

    private IReadOnlyList<string> GetExtraSearchDirs()
    {
        string raw = mySettings.GetValue((IlSpySettings s) => s.AssemblyResolveDirs) ?? "";
        if (raw.Length == 0) return Array.Empty<string>();
        string[] parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (TryNormalizeSearchDir(part, out string canonical, out string? rejection))
            {
                result.Add(canonical);
            }
            else if (rejection != null)
            {
                ourLogger.Warn(rejection);
            }
        }
        return result;
    }

    // Delegates to IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir
    // — kept here as a thin wrapper so the call site stays local to the
    // foreach and the helper class can be tested without ReSharper SDK load.
    private static bool TryNormalizeSearchDir(string raw, out string canonical, out string? rejection)
        => IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir(raw, out canonical, out rejection);

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
            if (myEntries.ContainsKey(entry.Moniker)) return;
            TrackEntry(entry.Moniker, entry);
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

    public ExtendedDebugData? GetTypeDebugData(ICompiledElement type, ITaskExecutor taskExecutor)
    {
        return null;
    }

    public ExtendedDebugData? GetSourceDebugData(FileSystemPath file)
    {
        return null;
    }

    public bool IsPreferredForGettingDebugData(FileSystemPath file)
    {
        return myCache.CanBeCachedFile(Id, file);
    }

    private DecompilationCacheItem? DecompileToCacheItem(ICompiledElement compiledElement, ITaskExecutor taskExecutor)
    {
        try
        {
            ITypeElement? top = GetTopLevelTypeElement(compiledElement);
            if (top == null) return null;

            IAssembly? assembly = top.Module.ContainingProjectModule as IAssembly;
            if (assembly == null) return null;

            FileSystemPath? assemblyFile = null;
            foreach (IAssemblyFile candidate in assembly.GetFiles())
            {
                FileSystemPath? candidatePath = candidate.Location.AssemblyPhysicalPath?.ToNativeFileSystemPath();
                if (candidatePath == null || candidatePath.IsEmpty || !candidatePath.ExistsFile) continue;
                bool isRef = candidatePath.FullPath.Contains("/ref/") || candidatePath.FullPath.Contains("\\ref\\") || candidatePath.FullPath.Contains(".ref/");
                if (assemblyFile == null) assemblyFile = candidatePath;
                if (!isRef) { assemblyFile = candidatePath; break; }
            }
            if (assemblyFile == null) return null;

            IClrTypeName? clrName = top.GetClrName();
            if (clrName == null) return null;
            string fullName = clrName.FullName;
            if (string.IsNullOrEmpty(fullName)) return null;

            IlSpyOutputMode mode = ResolveEffectiveMode();
            string moniker = MonikerUtil.GetTypeCacheMoniker(top);
            string fileName = (top.ShortName ?? "Decompiled") + ".cs";

            myEntries.TryGetValue(moniker, out TypeDecompileEntry? trackedEntry);
            bool sameMode = trackedEntry != null && trackedEntry.Mode == mode;
            DecompilationCacheItem? cached = myCache.GetCacheItem(Id, assembly, moniker, fileName);
            if (cached != null && !cached.Expired && sameMode) return cached;

            DecompilerSettings decompilerSettings = BuildDecompilerSettings();
            IReadOnlyList<string> extraSearchDirs = GetExtraSearchDirs();
            bool showBanner = mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner);
            bool preferSourceLink = mySettings.GetValue((IlSpySettings s) => s.PreferSourceLink);
            int sourceLinkTimeout = mySettings.GetValue((IlSpySettings s) => s.SourceLinkTimeoutSeconds);

            string content = string.Empty;
            bool fromSourceLink = false;
            using ManualResetEventSlim doneSignal = new ManualResetEventSlim(false);
            taskExecutor.ExecuteTask("Decompiling " + top.ShortName + " with ILSpy", TaskCancelable.Yes, _ =>
            {
                try
                {
                    // SourceLink fallback only applies to plain C# output. IL
                    // and mixed-mode views need ILSpy's disassembler regardless
                    // of how good the SourceLink source is.
                    if (preferSourceLink && mode == IlSpyOutputMode.CSharp)
                    {
                        string? sourceLinkContent = myDecompiler.TryGetSourceLinkSource(assemblyFile.FullPath, fullName, sourceLinkTimeout);
                        if (!string.IsNullOrEmpty(sourceLinkContent))
                        {
                            content = sourceLinkContent;
                            fromSourceLink = true;
                            return;
                        }
                    }
                    content = myDecompiler.DecompileType(assemblyFile.FullPath, fullName, decompilerSettings, extraSearchDirs, mode);
                }
                finally
                {
                    doneSignal.Set();
                }
            });

            if (!doneSignal.Wait(TimeSpan.FromMinutes(2))) return null;

            // Banner is only meaningful for ILSpy output. SourceLink already
            // returns the upstream file verbatim — prepending decompile
            // diagnostics there would just shift the real line numbers down
            // (annoying for "go to line N" navigation) without adding signal.
            if (!fromSourceLink)
            {
                AssemblyBannerMetadata? bannerMeta = showBanner ? myDecompiler.GetAssemblyBannerMetadata(assemblyFile.FullPath) : null;
                content = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(showBanner, bannerMeta, assemblyFile.FullPath, fullName, mode, extraSearchDirs, content);
            }
            IDictionary<string, string> properties = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, assemblyFile.FullPath, fullName, moniker, fileName);
            DecompilationCacheItem? result = myCache.PutCacheItem(Id, assembly, moniker, fileName, properties, content, sourceDebugData: null);
            if (result != null)
            {
                TrackEntry(moniker, new TypeDecompileEntry(assembly, assemblyFile.FullPath, fullName, moniker, fileName, mode));
            }
            return result;
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.DecompileToCacheItem failed");
            return null;
        }
    }

    private void TrackEntry(string moniker, TypeDecompileEntry entry)
    {
        lock (myEntriesAccessLock)
        {
            if (myEntries.ContainsKey(moniker))
            {
                myEntriesOrder.Remove(moniker);
            }
            myEntries[moniker] = entry;
            myEntriesOrder.AddLast(moniker);
            while (myEntriesOrder.Count > MaxTrackedTypes)
            {
                LinkedListNode<string>? first = myEntriesOrder.First;
                if (first == null) break;
                myEntriesOrder.RemoveFirst();
                myEntries.TryRemove(first.Value, out _);
            }
        }
    }

    // Inverse of BuildCacheProperties — returns null when any required key is
    // missing or the mode is unparseable.
    private static TypeDecompileEntry? TryParseEntry(IDictionary<string, string>? properties, IAssembly assembly)
    {
        if (properties == null) return null;
        if (!properties.TryGetValue("RiderIlSpy.Moniker", out string? moniker) || string.IsNullOrEmpty(moniker)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Assembly", out string? asmPath)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Type", out string? typeFullName)) return null;
        if (!properties.TryGetValue("RiderIlSpy.FileName", out string? fileName)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Mode", out string? modeStr)) return null;
        if (!Enum.TryParse(modeStr, out IlSpyOutputMode mode)) return null;
        return new TypeDecompileEntry(assembly, asmPath, typeFullName, moniker, fileName, mode);
    }

    private IlSpyOutputMode? ReadRdMode()
    {
        string? current = myRiderIlSpyModel.Mode.Value;
        if (string.IsNullOrEmpty(current)) return null;
        // Wire strings are encoded as IlSpyOutputMode member names by the
        // kotlin frontend (see IlSpyMode.backendName). Single Enum.TryParse
        // covers all current modes and any future additions automatically.
        return Enum.TryParse(current, out IlSpyOutputMode mode) ? mode : (IlSpyOutputMode?)null;
    }

    // Canonical mode-resolution seam: prefer the live wire value when present,
    // fall back to the persisted setting otherwise. Documented once in
    // RiderIlSpyModel.kt and centralized here so DecompileToCacheItem and
    // RedecompileAllEntries agree on the policy by construction.
    private IlSpyOutputMode ResolveEffectiveMode()
        => ReadRdMode() ?? mySettings.GetValue((IlSpySettings s) => s.OutputMode);

    private void OnRiderIlSpyModeChanged(string? newMode)
    {
        if (string.IsNullOrEmpty(newMode)) return;

        CancellationTokenSource newCts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (myRedecompileLock)
        {
            previous = myActiveRedecompileCts;
            myActiveRedecompileCts = newCts;
        }
        SafeCancelAndDispose(previous);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(75, newCts.Token).ConfigureAwait(false);
                RedecompileAllEntries(newCts.Token);
                myRiderIlSpyModel.ReadyTick.Fire(DateTime.UtcNow.Ticks);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer mode change; the next OnRiderIlSpyModeChanged will redrive the redecompile.
                // Logging at Verbose only — frequent during rapid mode toggles, not actionable.
                ourLogger.Verbose("RiderIlSpy.OnRiderIlSpyModeChanged: redecompile cancelled by newer mode change");
            }
            catch (Exception ex)
            {
                ourLogger.Error(ex, "RiderIlSpy.RedecompileAllEntries failed");
            }
            finally
            {
                lock (myRedecompileLock)
                {
                    if (ReferenceEquals(myActiveRedecompileCts, newCts))
                        myActiveRedecompileCts = null;
                }
                SafeCancelAndDispose(newCts);
            }
        });
    }

    // Cancel + dispose a CancellationTokenSource while tolerating the
    // ObjectDisposedException race with whichever task path got there first.
    private static void SafeCancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed by its task's finally */ }
        try { cts.Dispose(); } catch (ObjectDisposedException) { /* already disposed by a concurrent cancel-and-swap path */ }
    }

    private void RedecompileAllEntries(CancellationToken cancellationToken)
    {
        if (myEntries.IsEmpty) return;

        IlSpyOutputMode mode = ResolveEffectiveMode();
        DecompilerSettings decompilerSettings = BuildDecompilerSettings();
        IReadOnlyList<string> extraSearchDirs = GetExtraSearchDirs();
        bool showBanner = mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner);

        foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDecompileEntry entry = kv.Value;
            try
            {
                string content = myDecompiler.DecompileType(entry.AssemblyFilePath, entry.TypeFullName, decompilerSettings, extraSearchDirs, mode);
                AssemblyBannerMetadata? bannerMeta = showBanner ? myDecompiler.GetAssemblyBannerMetadata(entry.AssemblyFilePath) : null;
                content = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(showBanner, bannerMeta, entry.AssemblyFilePath, entry.TypeFullName, mode, extraSearchDirs, content);

                IDictionary<string, string> properties = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, entry.AssemblyFilePath, entry.TypeFullName, entry.Moniker, entry.FileName);
                myCache.PutCacheItem(Id, entry.Assembly, entry.Moniker, entry.FileName, properties, content, sourceDebugData: null);
                entry.Mode = mode;
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

    private sealed class TypeDecompileEntry
    {
        public IAssembly Assembly { get; }
        public string AssemblyFilePath { get; }
        public string TypeFullName { get; }
        public string Moniker { get; }
        public string FileName { get; }
        public IlSpyOutputMode Mode { get; set; }

        public TypeDecompileEntry(IAssembly assembly, string assemblyFilePath, string typeFullName, string moniker, string fileName, IlSpyOutputMode mode)
        {
            Assembly = assembly;
            AssemblyFilePath = assemblyFilePath;
            TypeFullName = typeFullName;
            Moniker = moniker;
            FileName = fileName;
            Mode = mode;
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
