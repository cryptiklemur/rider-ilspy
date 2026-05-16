namespace RiderIlSpy;

/// <summary>
/// Return shape for <see cref="IlSpyDecompiler.DecompileAssemblyToProject"/>.
/// Records the output directory the project decompiler wrote into plus a couple
/// of summary counts so the caller can show a "wrote N files, project at X"
/// confirmation without re-enumerating the output tree.
/// </summary>
/// <param name="OutputDirectory">Absolute path that was passed as the project root.</param>
/// <param name="ProjectFilePath">Absolute path to the generated .csproj, or null if ILSpy
/// emitted source without a project (rare — happens for module-only assemblies).</param>
/// <param name="CSharpFileCount">Number of .cs files written under <paramref name="OutputDirectory"/>.</param>
public sealed record DecompileAssemblyToProjectResult(
    string OutputDirectory,
    string? ProjectFilePath,
    int CSharpFileCount);
