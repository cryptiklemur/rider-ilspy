using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class TokenQueryHandlerTests
{
    [Fact]
    public void Parses_0x_Form()
    {
        Assert.True(TokenQueryHandler.TryParse("0x06000042", out int tok, out string? asm));
        Assert.Equal(0x06_000_042, tok);
        Assert.Null(asm);
    }

    [Fact]
    public void Parses_Hash_Form()
    {
        Assert.True(TokenQueryHandler.TryParse("#06000042", out int tok, out string? _));
        Assert.Equal(0x06_000_042, tok);
    }

    [Fact]
    public void Parses_Scoped_Form()
    {
        Assert.True(TokenQueryHandler.TryParse("MyAsm.dll#06000042", out int tok, out string? asm));
        Assert.Equal("MyAsm.dll", asm);
    }

    [Fact]
    public void Rejects_Garbage()
    {
        Assert.False(TokenQueryHandler.TryParse("not-a-token", out int _, out string? _));
    }
}
