using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace RiderIlSpy;

/// <summary>
/// Isolates the decompiler engine from Rider's ReSharperHost. The host process
/// already has ICSharpCode.Decompiler (old, max C# 11) plus
/// System.Reflection.Metadata / System.Collections.Immutable 8.0 loaded in the
/// default context; the engine needs decompiler 10.x with SRM/SCI 9.0. .NET
/// binds by simple assembly name within a context, so the only way to run both
/// is a separate AssemblyLoadContext: this context serves the four isolated
/// assemblies from the plugin's engine directory and returns null for
/// everything else, which falls through to the default context — that keeps
/// RiderIlSpy.Contracts types shared (one identity on both sides of the
/// boundary) while the engine's foreign deps stay private.
/// </summary>
internal sealed class IlSpyEngineLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> ourIsolatedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "RiderIlSpy.Engine",
        "ICSharpCode.Decompiler",
        "System.Reflection.Metadata",
        "System.Collections.Immutable",
    };

    private readonly string myEngineDirectory;

    public IlSpyEngineLoadContext(string engineDirectory) : base("RiderIlSpy.Engine")
    {
        myEngineDirectory = engineDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null || !ourIsolatedAssemblies.Contains(assemblyName.Name))
            return null;
        string candidate = Path.Combine(myEngineDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}
