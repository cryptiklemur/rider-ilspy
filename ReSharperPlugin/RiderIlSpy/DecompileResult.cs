namespace RiderIlSpy;

/// <summary>
/// Outcome of a decompile request. Replaces the prior shape where
/// <see cref="IlSpyDecompiler.DecompileType"/> returned a bare string that
/// callers could not programmatically distinguish from a comment-prefixed
/// failure. Mirrors <see cref="SourceLinkAttempt"/>'s typed-result pattern so
/// the two sibling APIs use the same shape.
/// </summary>
/// <param name="Content">The text Rider should display to the user. Always
/// non-null; for failure outcomes it is the comment-formatted exception trace
/// so users can still read the failure in the decompiled-source pane.</param>
/// <param name="Success">True when decompile (or IL fallback) produced real
/// source bytes; false when all paths bailed and <see cref="Content"/> is a
/// pure comment block. Callers that want to suppress caching or surface a
/// distinct error UI can branch on this.</param>
/// <param name="FailureReason">Short identifier describing why the decompile
/// failed (e.g. <c>"ArgumentException: fieldCount"</c>). Null on
/// <see cref="Success"/>; otherwise a one-line summary suitable for log entries
/// or telemetry without parsing the full <see cref="Content"/> comment block.</param>
public sealed record DecompileResult(string Content, bool Success, string? FailureReason)
{
    public static DecompileResult Ok(string content) => new DecompileResult(content, Success: true, FailureReason: null);

    public static DecompileResult Fail(string content, string reason) => new DecompileResult(content, Success: false, FailureReason: reason);
}
