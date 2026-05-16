using Xunit;

namespace RiderIlSpy.Tests;

public class SourceLinkMappingTests
{
    [Fact]
    public void TryParse_returns_null_for_empty_or_null_input()
    {
        Assert.Null(SourceLinkMapping.TryParse(""));
        Assert.Null(SourceLinkMapping.TryParse("   "));
    }

    [Fact]
    public void TryParse_returns_null_for_malformed_json()
    {
        Assert.Null(SourceLinkMapping.TryParse("{not json"));
    }

    [Fact]
    public void TryParse_returns_null_when_documents_key_missing()
    {
        Assert.Null(SourceLinkMapping.TryParse("{\"other\":{}}"));
    }

    [Fact]
    public void TryParse_returns_null_when_documents_object_is_empty()
    {
        Assert.Null(SourceLinkMapping.TryParse("{\"documents\":{}}"));
    }

    [Fact]
    public void ResolveUrl_expands_wildcard_for_unix_paths()
    {
        string json = "{\"documents\":{\"/_/*\":\"https://raw.githubusercontent.com/dotnet/runtime/abc123/*\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        Assert.Equal(
            "https://raw.githubusercontent.com/dotnet/runtime/abc123/src/libraries/System.Private.CoreLib/src/System/String.cs",
            m!.ResolveUrl("/_/src/libraries/System.Private.CoreLib/src/System/String.cs"));
    }

    [Fact]
    public void ResolveUrl_expands_wildcard_for_windows_paths()
    {
        string json = "{\"documents\":{\"C:\\\\proj\\\\*\":\"https://example.com/proj/*\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        // Document paths from a Windows PDB use backslashes; mapping must
        // produce forward-slash URLs for HTTP.
        Assert.Equal(
            "https://example.com/proj/src/A/B.cs",
            m!.ResolveUrl("C:\\proj\\src\\A\\B.cs"));
    }

    [Fact]
    public void ResolveUrl_picks_longest_matching_prefix()
    {
        string json = "{\"documents\":{" +
            "\"/_/*\":\"https://generic.example/*\"," +
            "\"/_/src/specific/*\":\"https://specific.example/*\"" +
            "}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        Assert.Equal(
            "https://specific.example/file.cs",
            m!.ResolveUrl("/_/src/specific/file.cs"));
    }

    [Fact]
    public void ResolveUrl_returns_null_when_no_rule_matches()
    {
        string json = "{\"documents\":{\"/_/src/*\":\"https://example.com/*\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        Assert.Null(m!.ResolveUrl("/somewhere/else.cs"));
    }

    [Fact]
    public void ResolveUrl_handles_mixed_separators_between_pdb_and_pattern()
    {
        // PDB recorded slashes one way, SourceLink JSON the other — common when
        // a Windows-built assembly is read on Linux without normalization.
        string json = "{\"documents\":{\"C:\\\\src\\\\*\":\"https://example.com/*\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        Assert.Equal(
            "https://example.com/file.cs",
            m!.ResolveUrl("C:/src/file.cs"));
    }

    [Fact]
    public void ResolveUrl_returns_null_for_empty_document_path()
    {
        string json = "{\"documents\":{\"/*\":\"https://example.com/*\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.NotNull(m);
        Assert.Null(m!.ResolveUrl(""));
    }

    [Fact]
    public void TryParse_drops_rules_with_unbalanced_wildcards()
    {
        // Pattern has *, replacement does not — rule is invalid per SourceLink
        // spec because there's nowhere to splice the tail in.
        string json = "{\"documents\":{\"/_/*\":\"https://example.com/fixed.cs\"}}";
        SourceLinkMapping? m = SourceLinkMapping.TryParse(json);
        Assert.Null(m);
    }
}
