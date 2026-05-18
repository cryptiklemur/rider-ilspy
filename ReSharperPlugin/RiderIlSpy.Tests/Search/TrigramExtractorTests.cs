using System.Collections.Generic;
using System.Linq;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class TrigramExtractorTests
{
    [Fact]
    public void Extracts_All_Length3_Substrings_Lowercase()
    {
        HashSet<string> tg = TrigramExtractor.Extract("Hello", caseSensitive: false);
        string[] expected = ["hel", "ell", "llo"];
        Assert.True(expected.ToHashSet().SetEquals(tg));
    }

    [Fact]
    public void Distinct()
    {
        HashSet<string> tg = TrigramExtractor.Extract("ababab", caseSensitive: false);
        string[] expected = ["aba", "bab"];
        Assert.True(expected.ToHashSet().SetEquals(tg));
    }

    [Fact]
    public void Empty_For_Inputs_Shorter_Than_3()
    {
        Assert.Empty(TrigramExtractor.Extract("ab", caseSensitive: false));
    }

    [Fact]
    public void Case_Sensitive_Mode_Preserves_Case()
    {
        HashSet<string> tg = TrigramExtractor.Extract("AbC", caseSensitive: true);
        string[] expected = ["AbC"];
        Assert.True(expected.ToHashSet().SetEquals(tg));
    }
}
