using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class SymbolSearchKindsTests
{
    [Theory]
    [InlineData("TypeAndMember", SymbolSearchKind.TypeAndMember)]
    [InlineData("Type", SymbolSearchKind.Type)]
    [InlineData("Member", SymbolSearchKind.Member)]
    [InlineData("Method", SymbolSearchKind.Method)]
    [InlineData("Field", SymbolSearchKind.Field)]
    [InlineData("Property", SymbolSearchKind.Property)]
    [InlineData("Event", SymbolSearchKind.Event)]
    [InlineData("Namespace", SymbolSearchKind.Namespace)]
    public void Maps_Every_Symbol_Query_Type(string queryType, SymbolSearchKind expected)
    {
        Assert.Equal(expected, SymbolSearchKinds.FromQueryType(queryType));
    }

    [Theory]
    [InlineData("Constant")]
    [InlineData("Token")]
    [InlineData("Resource")]
    [InlineData("Assembly")]
    [InlineData("Bogus")]
    [InlineData("")]
    public void Returns_None_For_Non_Symbol_Query_Types(string queryType)
    {
        Assert.Equal(SymbolSearchKind.None, SymbolSearchKinds.FromQueryType(queryType));
    }

    [Fact]
    public void Member_Excludes_Type()
    {
        Assert.False(SymbolSearchKind.Member.HasFlag(SymbolSearchKind.Type));
        Assert.True(SymbolSearchKind.TypeAndMember.HasFlag(SymbolSearchKind.Type));
    }
}
