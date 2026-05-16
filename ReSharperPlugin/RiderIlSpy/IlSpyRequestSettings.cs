using System.Collections.Generic;
using ICSharpCode.Decompiler;

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
/// <param name="DecompilerSettings">Fully populated ICSharpCode.Decompiler settings including language-version downgrade.</param>
/// <param name="ExtraSearchDirs">Normalized assembly-resolve directories (rejected entries already filtered + logged).</param>
/// <param name="ShowBanner">Whether to prepend the diagnostic banner to ILSpy output (no-op when content came from SourceLink).</param>
/// <param name="PreferSourceLink">Whether to attempt SourceLink before falling back to ILSpy decompilation.</param>
/// <param name="SourceLinkTimeoutSeconds">HTTP timeout for the SourceLink fetch; ignored when <see cref="PreferSourceLink"/> is false.</param>
public sealed record IlSpyRequestSettings(
    IlSpyOutputMode Mode,
    DecompilerSettings DecompilerSettings,
    IReadOnlyList<string> ExtraSearchDirs,
    bool ShowBanner,
    bool PreferSourceLink,
    int SourceLinkTimeoutSeconds);
