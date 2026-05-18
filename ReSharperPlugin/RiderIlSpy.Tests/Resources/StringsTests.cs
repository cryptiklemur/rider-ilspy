using RiderIlSpy.Resources;
using Xunit;

namespace RiderIlSpy.Tests.Resources;

public class StringsTests
{
    [Fact]
    public void OptionsPage_DisplayName_Resolves_NonEmpty()
    {
        // Resource manager returns the key itself if the .resx isn't embedded
        // with the expected LogicalName, so asserting we got something other
        // than the bare key proves the embedded resource lookup wired up.
        string value = Strings.OptionsPage_DisplayName;
        Assert.NotEqual(nameof(Strings.OptionsPage_DisplayName), value);
        Assert.False(string.IsNullOrEmpty(value));
    }

    [Fact]
    public void Search_UnknownQueryType_Formats_Argument_Into_Message()
    {
        string value = Strings.Search_UnknownQueryType("Whatzit");
        Assert.Contains("Whatzit", value);
        // The format template lives in the resx as "Unknown queryType: {0}";
        // a missing placeholder substitution would leave "{0}" in the result.
        Assert.DoesNotContain("{0}", value);
    }

    [Fact]
    public void OptionsPage_Header_OutputMode_Matches_English_Baseline()
    {
        // Pins the English baseline so a typo in the resx is caught at test
        // time rather than during a screenshot review.
        Assert.Equal("Output mode", Strings.OptionsPage_Header_OutputMode);
    }
}
