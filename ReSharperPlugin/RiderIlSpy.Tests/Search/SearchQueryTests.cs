using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class SearchQueryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   \n ")]
    [InlineData(null)]
    public void Blank_Input_Is_Not_Runnable(string? input)
    {
        Assert.False(SearchQuery.IsRunnable(input));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("StringBuilder")]
    [InlineData(" padded ")]
    public void Non_Blank_Input_Is_Runnable(string input)
    {
        Assert.True(SearchQuery.IsRunnable(input));
    }

    [Fact]
    public void Blank_Input_Would_Otherwise_Match_Everything()
    {
        TextMatcher matcher = new TextMatcher("", caseSensitive: false, regex: false, wholeWord: false);

        Assert.True(matcher.Matches("anything at all"));
        Assert.False(SearchQuery.IsRunnable(""));
    }
}
