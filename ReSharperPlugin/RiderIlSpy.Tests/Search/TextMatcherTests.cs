using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class TextMatcherTests
{
    [Fact]
    public void Substring_Match_Ignores_Case_By_Default()
    {
        TextMatcher matcher = new TextMatcher("needle", caseSensitive: false, regex: false, wholeWord: false);

        Assert.True(matcher.Matches("hayNEEDLEstack"));
    }

    [Fact]
    public void Case_Sensitive_Rejects_Different_Case()
    {
        TextMatcher matcher = new TextMatcher("needle", caseSensitive: true, regex: false, wholeWord: false);

        Assert.False(matcher.Matches("hayNEEDLEstack"));
        Assert.True(matcher.Matches("hayneedlestack"));
    }

    [Fact]
    public void Whole_Word_Rejects_Embedded_Match()
    {
        TextMatcher matcher = new TextMatcher("needle", caseSensitive: false, regex: false, wholeWord: true);

        Assert.False(matcher.Matches("hayneedlestack"));
        Assert.True(matcher.Matches("hay needle stack"));
    }

    [Fact]
    public void Regex_Mode_Treats_Input_As_Pattern()
    {
        TextMatcher matcher = new TextMatcher("ne+dle", caseSensitive: false, regex: true, wholeWord: false);

        Assert.True(matcher.Matches("neeeedle"));
        Assert.False(matcher.Matches("ndle"));
    }

    [Fact]
    public void Regex_Mode_Overrides_Whole_Word()
    {
        TextMatcher matcher = new TextMatcher("needle", caseSensitive: false, regex: true, wholeWord: true);

        Assert.True(matcher.Matches("hayneedlestack"));
    }
}
