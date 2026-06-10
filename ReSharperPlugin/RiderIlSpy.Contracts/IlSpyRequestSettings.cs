using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// Per-request snapshot of every IlSpySettings + rd-live value that drives one
/// decompile pass. Captured once at the start of <c>DecompileToCacheItem</c> /
/// <c>RedecompileAllEntriesAsync</c> / <c>OnSaveAsProjectRequest</c> so a settings
/// edit mid-pass cannot produce a partial view (e.g. mode resolved before a
/// toggle, banner toggle resolved after). Without this snapshot, three callers
/// re-derived identical state per pass and any in-flight settings write could
/// slice the configuration unpredictably.
/// </summary>
/// <param name="Mode">Effective output mode — rd-live value preferred, persisted setting fallback.</param>
/// <param name="DecompilerOptions">SDK-free decompiler option mirror including language-version downgrade; the engine converts it to ICSharpCode DecompilerSettings on its side of the load-context boundary.</param>
/// <param name="ExtraSearchDirs">Normalized assembly-resolve directories (rejected entries already filtered + logged).</param>
/// <param name="ShowBanner">Whether to prepend the diagnostic banner to ILSpy output (no-op when content came from SourceLink).</param>
/// <param name="PreferSourceLink">Whether to attempt SourceLink before falling back to ILSpy decompilation.</param>
/// <param name="SourceLinkTimeoutSeconds">HTTP timeout for the SourceLink fetch; ignored when <see cref="PreferSourceLink"/> is false.</param>
/// <param name="DecompileReferenceAssemblies">When false, the provider refuses to resolve to a ref-only assembly (paths with the SDK `/ref/` marker). The navigation falls through to Rider's built-in decompiler instead of producing ILSpy's empty-body stub output. When true, refs are allowed as fallback when no impl candidate exists.</param>
public sealed record IlSpyRequestSettings(
    IlSpyOutputMode Mode,
    IlSpyDecompilerOptions DecompilerOptions,
    IReadOnlyList<string> ExtraSearchDirs,
    bool ShowBanner,
    bool PreferSourceLink,
    int SourceLinkTimeoutSeconds,
    bool DecompileReferenceAssemblies);
