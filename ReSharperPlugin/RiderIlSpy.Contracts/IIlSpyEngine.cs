using System;
using System.Collections.Generic;
using System.Threading;
using RiderIlSpy.Search;

namespace RiderIlSpy;

/// <summary>
/// The full surface the host crosses into the isolated decompiler engine
/// through. Implemented by <c>RiderIlSpy.Engine.IlSpyEngine</c>, which the host
/// instantiates inside a dedicated AssemblyLoadContext (see IlSpyEngineLoadContext)
/// so the engine's ICSharpCode.Decompiler / System.Reflection.Metadata /
/// System.Collections.Immutable versions never collide with the older copies
/// Rider bundles in ReSharperHost. Every parameter and return type here must be
/// a Contracts type or BCL type from the shared (default) load context — no
/// ICSharpCode or SRM types may appear in these signatures.
/// </summary>
public interface IIlSpyEngine
{
    /// <summary>Version of the engine's ICSharpCode.Decompiler ("major.minor.patch"), for the diagnostic banner.</summary>
    string GetDecompilerVersion();

    DecompileResult DecompileType(
        string assemblyPath,
        string typeFullName,
        IlSpyDecompilerOptions options,
        IReadOnlyList<string>? extraSearchDirs = null,
        IlSpyOutputMode mode = IlSpyOutputMode.CSharp);

    DecompileResult DecompileAssemblyInfo(
        string assemblyPath,
        IlSpyDecompilerOptions? options = null,
        IReadOnlyList<string>? extraSearchDirs = null);

    DecompileAssemblyToProjectResult DecompileAssemblyToProject(
        string assemblyPath,
        string targetDirectory,
        IlSpyDecompilerOptions options,
        IReadOnlyList<string>? extraSearchDirs = null,
        CancellationToken cancellationToken = default);

    AssemblyBannerMetadata? ReadAssemblyBannerMetadata(string assemblyPath);

    /// <summary>Null when the assembly has no readable PDB.</summary>
    PdbSourceLinkInfo? ReadPdbSourceLinkInfo(string assemblyPath, string typeFullName);

    IlSpyNavResolution ResolveNavigation(string assemblyPath, int metadataToken, int ilOffset);

    IlSpySearchIndex BuildSearchIndex(
        IEnumerable<string> assemblyPaths,
        Action<IlSpyIndexBuildProgress> onProgress,
        CancellationToken cancellationToken,
        Action<string, Exception>? onSkipped = null);

    /// <summary>Indexes one (changed) assembly's literals/attributes/resources into <paramref name="index"/>.</summary>
    void IndexAssembly(string assemblyPath, IlSpySearchIndex index);

    List<ConstantHit> ScanConstants(IReadOnlyList<string> assemblyPaths, string input);
}
