using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace RiderIlSpy.Tests;

// Pins the isolation contract IlSpyEngineHost relies on at runtime inside
// Rider: the engine and its ICSharpCode.Decompiler resolve inside the
// dedicated AssemblyLoadContext (from the engine directory), NOT from
// whatever the default context already has loaded. In the real host the
// default context holds Rider's bundled 8.2 decompiler; in this test the
// default context holds our 10.1 — either way the identities must differ,
// or the whole MissingMethodException class of bugs comes back.
public class IlSpyEngineLoadContextTests
{
    private static string TestBinDir => Path.GetDirectoryName(typeof(IlSpyEngineLoadContextTests).Assembly.Location)!;

    private static IIlSpyEngine LoadEngine(out Assembly engineAssembly)
    {
        IlSpyEngineLoadContext context = new IlSpyEngineLoadContext(TestBinDir);
        engineAssembly = context.LoadFromAssemblyPath(Path.Combine(TestBinDir, "RiderIlSpy.Engine.dll"));
        Type engineType = engineAssembly.GetType("RiderIlSpy.IlSpyEngine")!;
        return (IIlSpyEngine)Activator.CreateInstance(engineType)!;
    }

    [Fact]
    public void Engine_loads_in_isolated_context_and_reports_its_own_decompiler_version()
    {
        IIlSpyEngine engine = LoadEngine(out Assembly engineAssembly);
        Assert.NotEqual(typeof(IlSpyEngine).Assembly, engineAssembly);
        Assert.StartsWith("10.", engine.GetDecompilerVersion());
    }

    [Fact]
    public void Isolated_decompiler_identity_differs_from_default_context_copy()
    {
        IIlSpyEngine engine = LoadEngine(out Assembly engineAssembly);
        // Assembly loading is lazy — touch a decompiler-backed member so
        // ICSharpCode.Decompiler actually resolves into the isolated context.
        Assert.StartsWith("10.", engine.GetDecompilerVersion());
        Assembly isolatedDecompiler = AssemblyLoadContextOf(engineAssembly)
            .Assemblies.Single(a => a.GetName().Name == "ICSharpCode.Decompiler");
        Assembly defaultDecompiler = typeof(ICSharpCode.Decompiler.CSharp.CSharpDecompiler).Assembly;
        Assert.NotSame(defaultDecompiler, isolatedDecompiler);
    }

    [Fact]
    public void Contract_types_stay_shared_across_the_boundary()
    {
        IIlSpyEngine engine = LoadEngine(out _);
        // DecompileResult crossing back as a Contracts type (no cast failure)
        // proves Contracts resolved in the default context on both sides.
        DecompileResult result = engine.DecompileAssemblyInfo(typeof(IlSpyEngineLoadContextTests).Assembly.Location);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Content));
    }

    private static System.Runtime.Loader.AssemblyLoadContext AssemblyLoadContextOf(Assembly assembly)
        => System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly)!;
}
