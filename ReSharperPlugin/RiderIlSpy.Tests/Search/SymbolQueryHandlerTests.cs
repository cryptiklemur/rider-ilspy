using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class SymbolQueryHandlerTests
{
    private static PEFile OpenSymbols()
        => new PEFile(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "symbols.dll"));

    private static List<SymbolHit> Scan(SymbolSearchKind kinds, string input)
    {
        using PEFile pe = OpenSymbols();
        return new SymbolQueryHandler().Scan(
            [pe], kinds, new TextMatcher(input, caseSensitive: false, regex: false, wholeWord: false),
            CancellationToken.None);
    }

    [Fact]
    public void Type_Kind_Matches_Type_Names_Only()
    {
        List<SymbolHit> hits = Scan(SymbolSearchKind.Type, "Needle");

        Assert.Equal(["NeedleHandler", "NeedleType"], hits.Select(h => h.Name).OrderBy(n => n));
        Assert.Contains("Fixture.Symbols.NeedleType", hits.Select(h => h.DisplayName));
    }

    [Fact]
    public void Member_Kind_Matches_Members_But_Not_Types()
    {
        List<SymbolHit> hits = Scan(SymbolSearchKind.Member, "Needle");

        Assert.DoesNotContain(SymbolSearchKind.Type, hits.Select(h => h.Kind));
        Assert.Contains(SymbolSearchKind.Method, hits.Select(h => h.Kind));
        Assert.Contains(SymbolSearchKind.Field, hits.Select(h => h.Kind));
        Assert.Contains(SymbolSearchKind.Property, hits.Select(h => h.Kind));
        Assert.Contains(SymbolSearchKind.Event, hits.Select(h => h.Kind));
    }

    [Fact]
    public void Method_Kind_Qualifies_Display_Name_With_Declaring_Type()
    {
        List<SymbolHit> hits = Scan(SymbolSearchKind.Method, "NeedleMethod");

        SymbolHit hit = Assert.Single(hits);
        Assert.Equal("Fixture.Symbols.NeedleType.NeedleMethod", hit.DisplayName);
        Assert.NotEqual(0, hit.MetadataToken);
    }

    [Theory]
    [InlineData(SymbolSearchKind.Field, "NeedleField")]
    [InlineData(SymbolSearchKind.Property, "NeedleProperty")]
    [InlineData(SymbolSearchKind.Event, "NeedleEvent")]
    public void Single_Kind_Returns_Only_That_Kind(SymbolSearchKind kind, string name)
    {
        List<SymbolHit> hits = Scan(kind, name);

        SymbolHit hit = Assert.Single(hits, h => h.Name == name);
        Assert.Equal(kind, hit.Kind);
    }

    [Fact]
    public void Namespace_Kind_Returns_One_Hit_Per_Namespace_With_No_Token()
    {
        List<SymbolHit> hits = Scan(SymbolSearchKind.Namespace, "Fixture");

        SymbolHit hit = Assert.Single(hits);
        Assert.Equal("Fixture.Symbols", hit.Name);
        Assert.Equal(0, hit.MetadataToken);
    }

    [Fact]
    public void TypeAndMember_Kind_Covers_Types_And_Members_But_Not_Namespaces()
    {
        List<SymbolHit> hits = Scan(SymbolSearchKind.TypeAndMember, "Needle");

        Assert.Contains(SymbolSearchKind.Type, hits.Select(h => h.Kind));
        Assert.Contains(SymbolSearchKind.Method, hits.Select(h => h.Kind));
        Assert.DoesNotContain(SymbolSearchKind.Namespace, hits.Select(h => h.Kind));
    }

    [Fact]
    public void Non_Matching_Input_Returns_Empty()
    {
        Assert.Empty(Scan(SymbolSearchKind.TypeAndMember, "NoSuchSymbolAnywhere"));
    }

    [Fact]
    public void Cancellation_Is_Observed()
    {
        using PEFile pe = OpenSymbols();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new SymbolQueryHandler().Scan(
            [pe], SymbolSearchKind.TypeAndMember,
            new TextMatcher("Needle", false, false, false), cts.Token));
    }
}
