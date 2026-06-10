using System;
using System.IO;
using System.Reflection;
using JetBrains.Application;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace RiderIlSpy;

/// <summary>
/// Owns the isolated engine: creates <see cref="IlSpyEngineLoadContext"/>,
/// loads RiderIlSpy.Engine.dll into it, and exposes the engine through the
/// SDK-free <see cref="IIlSpyEngine"/> contract. Components that used to take
/// IlSpyDecompiler directly now take this host and call <see cref="Engine"/>.
/// The engine assembly and its private ICSharpCode.Decompiler / SRM / SCI
/// copies live in the <c>engine/</c> subdirectory next to RiderIlSpy.dll —
/// a subdirectory rather than the plugin root so Rider's own plugin loader
/// never scans them into the default load context.
/// </summary>
[ShellComponent]
public class IlSpyEngineHost
{
    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyEngineHost>();
    private readonly Lazy<IIlSpyEngine> myEngine = new(CreateEngine);

    public IIlSpyEngine Engine => myEngine.Value;

    private static IIlSpyEngine CreateEngine()
    {
        string pluginDir = Path.GetDirectoryName(typeof(IlSpyEngineHost).Assembly.Location)
                           ?? throw new InvalidOperationException("RiderIlSpy.dll has no on-disk location; cannot locate the engine directory");
        string engineDir = Path.Combine(pluginDir, "engine");
        // Dev-loop convenience: `dotnet build` drops the engine flat next to the
        // host dll, while the packaged plugin ships it under engine/. Prefer the
        // packaged layout, fall back to flat.
        if (!File.Exists(Path.Combine(engineDir, "RiderIlSpy.Engine.dll")))
            engineDir = pluginDir;

        string enginePath = Path.Combine(engineDir, "RiderIlSpy.Engine.dll");
        IlSpyEngineLoadContext context = new IlSpyEngineLoadContext(engineDir);
        Assembly engineAssembly = context.LoadFromAssemblyPath(enginePath);
        Type engineType = engineAssembly.GetType("RiderIlSpy.IlSpyEngine")
                          ?? throw new InvalidOperationException("RiderIlSpy.IlSpyEngine not found in " + enginePath);
        IIlSpyEngine engine = (IIlSpyEngine)(Activator.CreateInstance(engineType)
                          ?? throw new InvalidOperationException("Activator returned null for " + engineType));
        ourLogger.Info("RiderIlSpy engine loaded in isolated context from " + enginePath
                       + " (ICSharpCode.Decompiler " + engine.GetDecompilerVersion() + ")");
        return engine;
    }
}
