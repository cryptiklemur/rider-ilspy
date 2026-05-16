namespace RiderIlSpy;

/// <summary>
/// Result of a single decompile-fetch attempt: the text Rider should show,
/// whether it came from SourceLink (vs ILSpy), and the typed
/// <see cref="SourceLinkOutcome"/> so the caller can decide whether to add a
/// banner row. Bundling these three closures-mutated locals into a single
/// typed return removes the prior pattern of a <c>ManualResetEventSlim</c>
/// closure with four parallel state variables, which read like a side-effect
/// soup at the call site.
/// </summary>
/// <param name="Content">Source text Rider should display. Empty string on
/// fetch failure (caller decides whether to cache it).</param>
/// <param name="FromSourceLink">True when SourceLink fired successfully, so
/// the caller knows to skip the diagnostic banner (which is only meaningful
/// over ILSpy output — SourceLink delivers upstream-verbatim source).</param>
/// <param name="SourceLinkOutcome">Typed outcome of the SourceLink attempt;
/// fed into the diagnostic banner so users can tell why we did or did not use
/// upstream source.</param>
public sealed record DecompileFetchOutcome(string Content, bool FromSourceLink, SourceLinkOutcome SourceLinkOutcome);
