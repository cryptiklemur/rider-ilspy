namespace RiderIlSpy;

/// <summary>
/// Result of a SourceLink fetch attempt. <see cref="Content"/> is the source
/// text when the fetch succeeded; otherwise it's <c>null</c> and
/// <see cref="Outcome"/> describes which step bailed (suitable for surfacing
/// in the diagnostic banner so the user can see why we fell back to ILSpy).
/// <see cref="Outcome"/> is a typed value so callers can pattern-match on
/// <see cref="SourceLinkStatus"/> instead of doing string equality.
/// </summary>
/// <remarks>
/// Lives at namespace scope alongside the other decompile-pipeline result
/// records (<see cref="DecompileResult"/>, <see cref="DecompileFetchOutcome"/>,
/// <see cref="SourceLinkOutcome"/>) instead of nesting inside
/// <see cref="IlSpyDecompiler"/> — sibling records mean callers reference one
/// stable namespace surface rather than one nested name per producer.
/// </remarks>
public sealed record SourceLinkAttempt(string? Content, SourceLinkOutcome Outcome);
