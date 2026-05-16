namespace RiderIlSpy;

/// <summary>
/// Stable identifier for the outcome of a SourceLink fetch attempt. Carried on
/// the SourceLink/ILSpy fork path so callers can pattern-match without parsing
/// free-form prose. Names are part of the public contract — adding a new value
/// is a non-breaking extension; renaming or removing one is a breaking change.
/// </summary>
public enum SourceLinkStatus
{
    /// <summary>Provider has not tried SourceLink yet for this request.</summary>
    NotAttempted,
    /// <summary>User turned off PreferSourceLink in IlSpySettings.</summary>
    Disabled,
    /// <summary>Mode is IL or CSharpWithIL — SourceLink only applies to plain CSharp output.</summary>
    SkippedMode,
    /// <summary>Assembly PDB is missing, non-portable, or unreadable.</summary>
    NoPdb,
    /// <summary>PDB carries no SourceLink CustomDebugInformation entry.</summary>
    NoSourceLinkEntry,
    /// <summary>SourceLink JSON in PDB does not parse.</summary>
    MalformedJson,
    /// <summary>Type spans multiple documents (partial class) or has no PDB rows.</summary>
    NoDocument,
    /// <summary>No mapping rule covers the document path.</summary>
    NoUrlMapping,
    /// <summary>HTTP fetch returned empty (404, timeout, DNS, etc).</summary>
    HttpFailed,
    /// <summary>Success — source was fetched from <see cref="SourceLinkOutcome.UsedUrl"/>.</summary>
    Used,
    /// <summary>Unexpected exception during the lookup. See <see cref="SourceLinkOutcome.ExceptionType"/>.</summary>
    Exception,
}

/// <summary>
/// Structured outcome of a SourceLink attempt. Replaces the prior free-form
/// Status string so callers can pattern-match on <see cref="Status"/> instead
/// of doing equality on magic strings. <see cref="UsedUrl"/> is populated only
/// when <see cref="Status"/> is <see cref="SourceLinkStatus.Used"/>;
/// <see cref="ExceptionType"/> only when <see cref="Status"/> is
/// <see cref="SourceLinkStatus.Exception"/>. All other Status values leave both
/// auxiliary fields null.
/// </summary>
public sealed record SourceLinkOutcome(SourceLinkStatus Status, string? UsedUrl, string? ExceptionType)
{
    public static SourceLinkOutcome Plain(SourceLinkStatus status) => new SourceLinkOutcome(status, null, null);

    public static SourceLinkOutcome UsedAt(string url) => new SourceLinkOutcome(SourceLinkStatus.Used, url, null);

    public static SourceLinkOutcome ExceptionOf(string typeName) => new SourceLinkOutcome(SourceLinkStatus.Exception, null, typeName);
}

/// <summary>
/// Maps a <see cref="SourceLinkOutcome"/> to its diagnostic-banner line. Pure
/// helper so the banner shape (which RiderIlSpy.Tests covers) stays unit-testable
/// without spinning up the IDE host.
/// </summary>
public static class SourceLinkOutcomeFormatter
{
    /// <summary>
    /// Returns the banner line for this outcome (without trailing newline), or
    /// null when the outcome should be silenced. <see cref="SourceLinkStatus.NotAttempted"/>,
    /// <see cref="SourceLinkStatus.Disabled"/>, and <see cref="SourceLinkStatus.SkippedMode"/>
    /// are silent — the toggle is visible in settings and "we used ILSpy" is
    /// implied by the banner's Mode line. Everything else carries diagnostic value.
    /// </summary>
    public static string? FormatBannerLine(SourceLinkOutcome outcome)
    {
        switch (outcome.Status)
        {
            case SourceLinkStatus.NotAttempted:
            case SourceLinkStatus.Disabled:
            case SourceLinkStatus.SkippedMode:
                return null;
            case SourceLinkStatus.Used:
                return outcome.UsedUrl is null
                    ? "// SourceLink: used"
                    : "// SourceLink: used: " + outcome.UsedUrl;
            case SourceLinkStatus.Exception:
                return outcome.ExceptionType is null
                    ? "// SourceLink: exception"
                    : "// SourceLink: exception: " + outcome.ExceptionType;
            case SourceLinkStatus.NoPdb: return "// SourceLink: no-pdb";
            case SourceLinkStatus.NoSourceLinkEntry: return "// SourceLink: no-sourcelink-entry-in-pdb";
            case SourceLinkStatus.MalformedJson: return "// SourceLink: malformed-sourcelink-json";
            case SourceLinkStatus.NoDocument: return "// SourceLink: no-document-for-type";
            case SourceLinkStatus.NoUrlMapping: return "// SourceLink: no-url-for-document";
            case SourceLinkStatus.HttpFailed: return "// SourceLink: http-fetch-failed";
            default: return "// SourceLink: " + outcome.Status.ToString();
        }
    }
}
