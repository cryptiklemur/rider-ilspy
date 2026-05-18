namespace RiderIlSpy;

/// <summary>
/// Return shape for <see cref="IlSpyDecompiler.DecompileAssemblyToProject"/>.
/// Mirrors <see cref="DecompileResult"/>'s Success/FailureReason discriminator
/// so callers can branch on outcome without inspecting filesystem state, and
/// so a thrown exception inside the project decompiler surfaces as a typed
/// failure (matching DecompileType and DecompileAssemblyInfo).
/// </summary>
/// <param name="OutputDirectory">Absolute path that was passed as the project
/// root. Always populated, even on failure, so callers can clean up partial
/// writes.</param>
/// <param name="ProjectFilePath">Absolute path to the generated .csproj, or
/// null if ILSpy emitted source without a project (rare — module-only
/// assemblies) or if decompilation failed before the project file was
/// written.</param>
/// <param name="CSharpFileCount">Number of .cs files written under
/// <paramref name="OutputDirectory"/>. Counts the partial output on
/// failure so callers can report "wrote N files before failing".</param>
/// <param name="Success">True when ILSpy completed the project decompile
/// without throwing. False when an exception was caught inside
/// DecompileAssemblyToProject.</param>
/// <param name="FailureReason">One-line summary of the exception (type name
/// + message) when <see cref="Success"/> is false; null otherwise. Suitable
/// for log entries or telemetry without holding onto the full exception.</param>
public sealed record DecompileAssemblyToProjectResult(
    string OutputDirectory,
    string? ProjectFilePath,
    int CSharpFileCount,
    bool Success,
    string? FailureReason)
{
    public static DecompileAssemblyToProjectResult Ok(string outputDirectory, string? projectFilePath, int csharpFileCount) =>
        new DecompileAssemblyToProjectResult(outputDirectory, projectFilePath, csharpFileCount, Success: true, FailureReason: null);

    public static DecompileAssemblyToProjectResult Fail(string outputDirectory, string? projectFilePath, int csharpFileCount, string reason) =>
        new DecompileAssemblyToProjectResult(outputDirectory, projectFilePath, csharpFileCount, Success: false, FailureReason: reason);
}
