namespace RiderIlSpy;

/// <summary>
/// Result of a single decompile-fetch attempt: the text Rider should show,
/// whether it came from SourceLink (vs ILSpy), the typed
/// <see cref="SourceLinkOutcome"/> so the caller can decide whether to add a
/// banner row, and the Success/FailureReason discriminator propagated from
/// the underlying <see cref="DecompileResult"/> (or set to Success=true for
/// SourceLink hits, which only return on success). Bundling these closures-
/// mutated locals into a single typed return removes the prior pattern of a
/// <c>ManualResetEventSlim</c> closure with parallel state variables that
/// read like a side-effect soup at the call site.
/// </summary>
/// <param name="Content">Source text Rider should display. For ILSpy failure
/// outcomes this is the comment-formatted exception trace (so the user can
/// still see what went wrong); the caller may choose to skip caching it
/// based on <see cref="Success"/>.</param>
/// <param name="FromSourceLink">True when SourceLink fired successfully, so
/// the caller knows to skip the diagnostic banner (which is only meaningful
/// over ILSpy output — SourceLink delivers upstream-verbatim source).</param>
/// <param name="SourceLinkOutcome">Typed outcome of the SourceLink attempt;
/// fed into the diagnostic banner so users can tell why we did or did not use
/// upstream source.</param>
/// <param name="Success">True when decompile (or SourceLink) produced real
/// source bytes; false when all paths bailed and <see cref="Content"/> is a
/// pure comment block. Callers branch on this to suppress caching of pure
/// failure output.</param>
/// <param name="FailureReason">One-line summary of the decompile failure
/// (type + message). Null when <see cref="Success"/> is true.</param>
public sealed record DecompileFetchOutcome(
    string Content,
    bool FromSourceLink,
    SourceLinkOutcome SourceLinkOutcome,
    bool Success,
    string? FailureReason);
