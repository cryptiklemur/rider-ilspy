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
    private const int MaxTrackedTypes = 512;

    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyExternalSourcesProvider>();

    private readonly INavigationDecompilationCache myCache;
    private readonly IlSpyDecompiler myDecompiler;
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly ConcurrentDictionary<string, TypeDecompileEntry> myEntries = new ConcurrentDictionary<string, TypeDecompileEntry>();
    private readonly object myEntriesAccessLock = new object();
    private readonly LinkedList<string> myEntriesOrder = new LinkedList<string>();
    private FileSystemWatcher? myWatcher;
    private CancellationTokenSource? myActiveRedecompileCts;
    private readonly object myRedecompileLock = new object();

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
        string[] parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("\\\\") || trimmed.StartsWith("//"))
            {
                ourLogger.Warn("RiderIlSpy: rejecting UNC/network search dir: {0}", trimmed);
                continue;
            }
            if (!Path.IsPathRooted(trimmed))
            {
                ourLogger.Warn("RiderIlSpy: rejecting non-absolute search dir: {0}", trimmed);
                continue;
            }
            string canonical;
            try { canonical = Path.GetFullPath(trimmed); }
            catch (Exception ex)
            {
                ourLogger.Warn("RiderIlSpy: rejecting unresolvable search dir '{0}': {1}", trimmed, ex.Message);
                continue;
            }
            if (!Directory.Exists(canonical))
            {
                ourLogger.Warn("RiderIlSpy: search dir does not exist: {0}", canonical);
                continue;
            }
            result.Add(canonical);
        }
        return result;
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
            if (item.Properties == null) return;
            if (!item.Properties.TryGetValue("RiderIlSpy.Moniker", out string? moniker)) return;
            if (string.IsNullOrEmpty(moniker)) return;
            if (myEntries.ContainsKey(moniker)) return;
            if (!item.Properties.TryGetValue("RiderIlSpy.Assembly", out string? asmPath)) return;
            if (!item.Properties.TryGetValue("RiderIlSpy.Type", out string? typeFullName)) return;
            if (!item.Properties.TryGetValue("RiderIlSpy.FileName", out string? fileName)) return;
            if (!item.Properties.TryGetValue("RiderIlSpy.Mode", out string? modeStr)) return;
            if (!Enum.TryParse(modeStr, out IlSpyOutputMode mode)) return;
            TrackEntry(moniker, new TypeDecompileEntry(item.Assembly, asmPath, typeFullName, moniker, fileName, mode));
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.TryRehydrateEntry failed");
        }
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
            using ManualResetEventSlim doneSignal = new ManualResetEventSlim(false);
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
                finally
                {
                    doneSignal.Set();
                }
            });

            if (!doneSignal.Wait(TimeSpan.FromMinutes(2))) return null;

            if (showBanner)
            {
                string banner = "// RiderIlSpy: " + RedactHome(assemblyFile.FullPath) + "\n// extra search dirs: " + (extraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", extraSearchDirs.Select(RedactHome))) + "\n\n";
                content = banner + content;
            }
            properties["RiderIlSpy.Mode"] = mode.ToString();
            properties["RiderIlSpy.Assembly"] = assemblyFile.FullPath;
            properties["RiderIlSpy.Type"] = fullName;
            properties["RiderIlSpy.Moniker"] = moniker;
            properties["RiderIlSpy.FileName"] = fileName;
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

    private static string RedactHome(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return path;
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path.Substring(home.Length);
        return path;
    }

    private static IlSpyOutputMode? ReadSharedModeOverride()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return null;
            string path = Path.Combine(home, ".RiderIlSpy", "mode.txt");
            if (!File.Exists(path)) return null;
            // Retry briefly: the writer may be mid-truncate-and-write; ATOMIC_MOVE on the
            // Kotlin side avoids this but the file can still be momentarily empty under
            // races against editor save events.
            string content = string.Empty;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { content = File.ReadAllText(path).Trim(); }
                catch (IOException) { content = string.Empty; }
                if (content.Length > 0) break;
                Thread.Sleep(20);
            }
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
            if (string.IsNullOrEmpty(home))
            {
                ourLogger.Warn("RiderIlSpy.SetupModeWatcher: HOME / user profile env not set; mode changes will not auto-refresh");
                return;
            }
            string dir = Path.Combine(home, ".RiderIlSpy");
            Directory.CreateDirectory(dir);
            FileSystemWatcher watcher = new FileSystemWatcher(dir, "mode.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnModeFileChanged;
            watcher.Created += OnModeFileChanged;
            watcher.Renamed += OnModeFileChanged;
            watcher.Error += (_, errArgs) =>
            {
                Exception? err = errArgs.GetException();
                ourLogger.Warn("RiderIlSpy.SetupModeWatcher: FileSystemWatcher error{0}; mode changes may stop refreshing until Rider restarts. If on Linux check `sysctl fs.inotify.max_user_watches`", err == null ? string.Empty : ": " + err.Message);
            };
            watcher.EnableRaisingEvents = true;
            myWatcher = watcher;
            lifetime.OnTermination(() =>
            {
                try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* ignore */ }
            });
        }
        catch (Exception ex)
        {
            ourLogger.Warn("RiderIlSpy.SetupModeWatcher failed to install watcher; falling back to per-navigation mode read. Mode changes will not auto-refresh already-open editors. {0}: {1}", ex.GetType().Name, ex.Message);
        }
    }

    private void OnModeFileChanged(object sender, FileSystemEventArgs e)
    {
        CancellationTokenSource newCts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (myRedecompileLock)
        {
            previous = myActiveRedecompileCts;
            myActiveRedecompileCts = newCts;
        }
        try { previous?.Cancel(); } catch { /* ignore */ }
        try { previous?.Dispose(); } catch { /* ignore */ }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(75, newCts.Token).ConfigureAwait(false);
                RedecompileAllEntries(newCts.Token);
                WriteReadySignal();
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer mode change; do nothing
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
                try { newCts.Dispose(); } catch { /* ignore */ }
            }
        });
    }

    private static void WriteReadySignal()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return;
            string dir = Path.Combine(home, ".RiderIlSpy");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "ready.txt");
            string tmp = path + ".tmp";
            // counter monotonically increments so file watchers always see size/lastwrite change
            long ticks = DateTime.UtcNow.Ticks;
            File.WriteAllText(tmp, ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { File.Move(tmp, path, overwrite: true); }
            catch (PlatformNotSupportedException)
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
        catch (Exception ex)
        {
            ourLogger.Error(ex, "RiderIlSpy.WriteReadySignal failed");
        }
    }

    private void RedecompileAllEntries(CancellationToken cancellationToken)
    {
        if (myEntries.IsEmpty) return;

        IlSpyOutputMode mode = ReadSharedModeOverride() ?? mySettings.GetValue((IlSpySettings s) => s.OutputMode);
        DecompilerSettings decompilerSettings = BuildDecompilerSettings();
        IReadOnlyList<string> extraSearchDirs = GetExtraSearchDirs();
        bool showBanner = mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner);

        foreach (KeyValuePair<string, TypeDecompileEntry> kv in myEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    string banner = "// RiderIlSpy: " + RedactHome(entry.AssemblyFilePath) + "\n// extra search dirs: " + (extraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", extraSearchDirs.Select(RedactHome))) + "\n\n";
                    content = banner + content;
                }

                IDictionary<string, string> properties = new Dictionary<string, string>
                {
                    ["RiderIlSpy.Mode"] = mode.ToString(),
                    ["RiderIlSpy.Assembly"] = entry.AssemblyFilePath,
                    ["RiderIlSpy.Type"] = entry.TypeFullName,
                    ["RiderIlSpy.Moniker"] = entry.Moniker,
                    ["RiderIlSpy.FileName"] = entry.FileName,
                };
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
