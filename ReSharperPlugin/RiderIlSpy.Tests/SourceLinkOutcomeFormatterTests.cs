using RiderIlSpy;
using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Pins the banner-line shape produced by <see cref="SourceLinkOutcomeFormatter"/>.
/// The output is part of the user-visible diagnostic banner contract — callers
/// pattern-match the line as a stable identifier (e.g. test assertions look for
/// `// SourceLink: no-pdb`), so a stray rename here would silently break the
/// diagnostic surface.
/// </summary>
public sealed class SourceLinkOutcomeFormatterTests
{
    [Theory]
    [InlineData(SourceLinkStatus.NotAttempted)]
    [InlineData(SourceLinkStatus.Disabled)]
    [InlineData(SourceLinkStatus.SkippedMode)]
    public void FormatBannerLine_silences_non_diagnostic_statuses(SourceLinkStatus status)
    {
        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(SourceLinkOutcome.Plain(status));

        Assert.Null(line);
    }

    [Theory]
    [InlineData(SourceLinkStatus.NoPdb, "// SourceLink: no-pdb")]
    [InlineData(SourceLinkStatus.NoSourceLinkEntry, "// SourceLink: no-sourcelink-entry-in-pdb")]
    [InlineData(SourceLinkStatus.MalformedJson, "// SourceLink: malformed-sourcelink-json")]
    [InlineData(SourceLinkStatus.NoDocument, "// SourceLink: no-document-for-type")]
    [InlineData(SourceLinkStatus.NoUrlMapping, "// SourceLink: no-url-for-document")]
    [InlineData(SourceLinkStatus.HttpFailed, "// SourceLink: http-fetch-failed")]
    public void FormatBannerLine_emits_kebab_identifier_for_failure_statuses(SourceLinkStatus status, string expected)
    {
        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(SourceLinkOutcome.Plain(status));

        Assert.Equal(expected, line);
    }

    [Fact]
    public void FormatBannerLine_used_includes_url_when_provided()
    {
        SourceLinkOutcome outcome = SourceLinkOutcome.UsedAt("https://raw.githubusercontent.com/foo/bar/abc/src/T.cs");

        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(outcome);

        Assert.Equal("// SourceLink: used: https://raw.githubusercontent.com/foo/bar/abc/src/T.cs", line);
    }

    [Fact]
    public void FormatBannerLine_used_without_url_falls_back_to_bare_label()
    {
        // Defensive: if a future caller constructs SourceLinkOutcome(Used, null, null)
        // by hand we still emit a valid banner line rather than crash.
        SourceLinkOutcome outcome = new SourceLinkOutcome(SourceLinkStatus.Used, UsedUrl: null, ExceptionType: null);

        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(outcome);

        Assert.Equal("// SourceLink: used", line);
    }

    [Fact]
    public void FormatBannerLine_exception_includes_type_when_provided()
    {
        SourceLinkOutcome outcome = SourceLinkOutcome.ExceptionOf("HttpRequestException");

        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(outcome);

        Assert.Equal("// SourceLink: exception: HttpRequestException", line);
    }

    [Fact]
    public void FormatBannerLine_exception_without_type_falls_back_to_bare_label()
    {
        SourceLinkOutcome outcome = new SourceLinkOutcome(SourceLinkStatus.Exception, UsedUrl: null, ExceptionType: null);

        string? line = SourceLinkOutcomeFormatter.FormatBannerLine(outcome);

        Assert.Equal("// SourceLink: exception", line);
    }

    [Fact]
    public void UsedAt_factory_sets_status_and_url()
    {
        SourceLinkOutcome outcome = SourceLinkOutcome.UsedAt("https://example.com/x.cs");

        Assert.Equal(SourceLinkStatus.Used, outcome.Status);
        Assert.Equal("https://example.com/x.cs", outcome.UsedUrl);
        Assert.Null(outcome.ExceptionType);
    }

    [Fact]
    public void ExceptionOf_factory_sets_status_and_type()
    {
        SourceLinkOutcome outcome = SourceLinkOutcome.ExceptionOf("TimeoutException");

        Assert.Equal(SourceLinkStatus.Exception, outcome.Status);
        Assert.Null(outcome.UsedUrl);
        Assert.Equal("TimeoutException", outcome.ExceptionType);
    }

    [Fact]
    public void Plain_factory_leaves_auxiliary_fields_null()
    {
        SourceLinkOutcome outcome = SourceLinkOutcome.Plain(SourceLinkStatus.NoPdb);

        Assert.Equal(SourceLinkStatus.NoPdb, outcome.Status);
        Assert.Null(outcome.UsedUrl);
        Assert.Null(outcome.ExceptionType);
    }
}
