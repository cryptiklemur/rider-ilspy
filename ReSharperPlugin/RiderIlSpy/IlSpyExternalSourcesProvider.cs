using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using JetBrains.Application.Parts;
using JetBrains.Application.Progress;
using JetBrains.Application.Settings;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Debug;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.ReSharper.Feature.Services.ExternalSource;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Utils;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace RiderIlSpy;

[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public class IlSpyExternalSourcesProvider : IExternalSourcesProvider
{
    private const string DecompilerIdConst = "RiderIlSpy";

    private readonly INavigationDecompilationCache myCache;
    private readonly IlSpyDecompiler myDecompiler;
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly ConcurrentDictionary<string, TypeDecompileEntry> myEntries = new ConcurrentDictionary<string, TypeDecompileEntry>();
    private FileSystemWatcher? myWatcher;
    private int myRedecompileScheduled;

    public IlSpyExternalSourcesProvider(Lifetime lifetime, ISettingsStore settingsStore, INavigationDecompilationCache cache, IlSpyDecompiler decompiler)
    {
        myCache = cache;
        myDecompiler = decompiler;
        mySettings = settingsStore.BindToContextLive(lifetime, ContextRange.ApplicationWide);
        SetupModeWatcher(lifetime);
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
        return new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = mySettings.GetValue((IlSpySettings s) => s.ThrowOnAssemblyResolveErrors),
            AsyncAwait = mySettings.GetValue((IlSpySettings s) => s.AsyncAwait),
            UseExpressionBodyForCalculatedGetterOnlyProperties = mySettings.GetValue((IlSpySettings s) => s.ExpressionBodies),
            NamedArguments = mySettings.GetValue((IlSpySettings s) => s.NamedArguments),
            ShowXmlDocumentation = mySettings.GetValue((IlSpySettings s) => s.ShowXmlDocumentation),
            RemoveDeadCode = mySettings.GetValue((IlSpySettings s) => s.RemoveDeadCode),
        };
    }

    private IReadOnlyList<string> GetExtraSearchDirs()
    {
        string raw = mySettings.GetValue((IlSpySettings s) => s.AssemblyResolveDirs) ?? "";
        if (raw.Length == 0) return Array.Empty<string>();
        string[] parts = raw.Split(new[] { ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0 && Directory.Exists(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    public ExternalSourcesMapping? MapFileToAssembly(FileSystemPath file)
    {
        if (!myCache.CanBeCachedFile(Id, file)) return null;
        DecompilationCacheItem? item = myCache.GetCacheItem(file);
        if (item == null) return null;
        return new ExternalSourcesMapping(item.Assembly, item.Location, this, isUserFile: false);
    }

    public IReadOnlyCollection<ExternalSourcesMapping>? NavigateToSources(ICompiledElement compiledElement, ITaskExecutor taskExecutor)
    {
        DecompilationCacheItem? item = DecompileToCacheItem(compiledElement, taskExecutor);
        if (item == null) return ImmutableArray<ExternalSourcesMapping>.Empty;
        return ImmutableArray.Create(new ExternalSourcesMapping(item.Assembly, item.Location, this, isUserFile: false));
    }

    public IReadOnlyCollection<ExternalSourcesMapping>? NavigateToSources(CompiledElementNavigationInfo navigationInfo, ITaskExecutor taskExecutor)
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

            IlSpyOutputMode mode = ReadSharedModeOverride() ?? mySettings.GetValue((IlSpySettings s) => s.OutputMode);
            string moniker = MonikerUtil.GetTypeCacheMoniker(top);
            string fileName = (top.ShortName ?? "Decompiled") + ".cs";
            IDictionary<string, string> properties = new Dictionary<string, string>();

            myEntries.TryGetValue(moniker, out TypeDecompileEntry? trackedEntry);
            bool sameMode = trackedEntry != null && trackedEntry.Mode == mode;
            DecompilationCacheItem? cached = myCache.GetCacheItem(Id, assembly, moniker, fileName);
            if (cached != null && !cached.Expired && sameMode) return cached;

            DecompilerSettings decompilerSettings = BuildDecompilerSettings();
            IReadOnlyList<string> extraSearchDirs = GetExtraSearchDirs();
            bool showBanner = mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner);

            string content = string.Empty;
            bool done = false;
            taskExecutor.ExecuteTask("Decompiling " + top.ShortName + " with ILSpy", TaskCancelable.Yes, _ =>
            {
                try
                {
                    content = myDecompiler.DecompileType(assemblyFile.FullPath, fullName, decompilerSettings, extraSearchDirs, mode);
                }
                catch (Exception ex)
                {
                    content = "// ILSpy decompile failed for " + fullName + "\n// " + ex.GetType().Name + ": " + ex.Message;
                }
                done = true;
            });

            if (showBanner)
            {
                string banner = "// RiderIlSpy: " + assemblyFile.FullPath + "\n// extra search dirs: " + (extraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", extraSearchDirs)) + "\n\n";
                content = banner + content;
            }

            if (!done) return null;
            DecompilationCacheItem? result = myCache.PutCacheItem(Id, assembly, moniker, fileName, properties, content, sourceDebugData: null);
            if (result != null)
            {
                myEntries[moniker] = new TypeDecompileEntry(assembly, assemblyFile.FullPath, fullName, moniker, fileName, mode);
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogException("RiderIlSpy.DecompileToCacheItem failed", ex);
            return null;
        }
    }

    private static IlSpyOutputMode? ReadSharedModeOverride()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return null;
            string path = Path.Combine(home, ".RiderIlSpy", "mode.txt");
            if (!File.Exists(path)) return null;
            string content = File.ReadAllText(path).Trim();
            return content switch
            {
                "CSharp" => IlSpyOutputMode.CSharp,
                "IL" => IlSpyOutputMode.IL,
                "CSharpWithIL" => IlSpyOutputMode.CSharpWithIL,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private void SetupModeWatcher(Lifetime lifetime)
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return;
            string dir = Path.Combine(home, ".RiderIlSpy");
            Directory.CreateDirectory(dir);
            FileSystemWatcher watcher = new FileSystemWatcher(dir, "mode.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnModeFileChanged;
            watcher.Created += OnModeFileChanged;
            watcher.Renamed += OnModeFileChanged;
            watcher.EnableRaisingEvents = true;
            myWatcher = watcher;
            lifetime.OnTermination(() =>
            {
                try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* ignore */ }
            });
        }
        catch (Exception ex)
        {
            Logger.LogException("RiderIlSpy.SetupModeWatcher failed", ex);
        }
    }

    private void OnModeFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Interlocked.Exchange(ref myRedecompileScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(75).ConfigureAwait(false);
                Interlocked.Exchange(ref myRedecompileScheduled, 0);
                RedecompileAllEntries();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref myRedecompileScheduled, 0);
                Logger.LogException("RiderIlSpy.RedecompileAllEntries failed", ex);
            }
        });
    }

    private void RedecompileAllEntries()
    {
        if (myEntries.IsEmpty) return;

        IlSpyOutputMode mode = ReadSharedModeOverride() ?? mySettings.GetValue((IlSpySettings s) => s.OutputMode);
        DecompilerSettings decompilerSettings = BuildDecompilerSettings();
        IReadOnlyList<string> extraSearchDirs = GetExtraSearchDirs();
        bool showBanner = mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner);

        foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntries)
        {
            TypeDecompileEntry entry = kv.Value;
            try
            {
                string content;
                try
                {
                    content = myDecompiler.DecompileType(entry.AssemblyFilePath, entry.TypeFullName, decompilerSettings, extraSearchDirs, mode);
                }
                catch (Exception ex)
                {
                    content = "// ILSpy decompile failed for " + entry.TypeFullName + "\n// " + ex.GetType().Name + ": " + ex.Message;
                }

                if (showBanner)
                {
                    string banner = "// RiderIlSpy: " + entry.AssemblyFilePath + "\n// extra search dirs: " + (extraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", extraSearchDirs)) + "\n\n";
                    content = banner + content;
                }

                IDictionary<string, string> properties = new Dictionary<string, string>();
                myCache.PutCacheItem(Id, entry.Assembly, entry.Moniker, entry.FileName, properties, content, sourceDebugData: null);
                entry.Mode = mode;
            }
            catch (Exception ex)
            {
                Logger.LogException("RiderIlSpy.RedecompileEntry failed for " + entry.TypeFullName, ex);
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
