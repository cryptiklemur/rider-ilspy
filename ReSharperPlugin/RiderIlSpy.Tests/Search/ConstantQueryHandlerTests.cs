using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class ConstantQueryHandlerTests
{
    private static PEFile OpenLiterals()
        => new PEFile(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll"));

    private static PEFile OpenConstants()
        => new PEFile(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "constants.dll"));

    [Fact]
    public void Returns_Empty_When_No_Constants_Match()
    {
        using PEFile pe = OpenLiterals();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "9999");
        Assert.Empty(hits);
    }

    // The next batch pins ConstantQueryHandler.DecodeConstant's switch over
    // ConstantTypeCode (ConstantQueryHandler.cs:38-48). The fixture
    // Fixture.Constants.Values has one const per branch: Int32 777, Int64
    // 777_000_000_000, bool true, string "needle777", char '7', double 777.25,
    // float 777.5. The unsupported sentinel branch is unreachable from C# const
    // declarations (only Decimal/IntPtr-style codes hit it) — tested via the
    // "no-match" path below where the search string does not appear in any
    // decoded value, which forces the switch to evaluate every row.

    [Fact]
    public void Int32_Branch_Decodes_Decimal_Form()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "777").Where(h => h.ParsedType == "Int32").ToList();
        Assert.Single(hits);
        Assert.Equal("777", hits[0].Value);
    }

    [Fact]
    public void Int64_Branch_Decodes_Decimal_Form()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "777").Where(h => h.ParsedType == "Int64").ToList();
        Assert.Single(hits);
        Assert.Equal("777000000000", hits[0].Value);
    }

    [Fact]
    public void Boolean_Branch_Decodes_To_True_String()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "True").Where(h => h.ParsedType == "Boolean").ToList();
        Assert.Single(hits);
        Assert.Equal("True", hits[0].Value);
    }

    [Fact]
    public void String_Branch_Decodes_Verbatim()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "needle777").Where(h => h.ParsedType == "String").ToList();
        Assert.Single(hits);
        Assert.Equal("needle777", hits[0].Value);
    }

    [Fact]
    public void Char_Branch_Decodes_To_Single_Character()
    {
        using PEFile pe = OpenConstants();
        // '7' is the only Char row in the fixture; the value is the single
        // character "7", so a substring search for "7" hits Char + every
        // numeric branch. Filter to the Char row.
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "7").Where(h => h.ParsedType == "Char").ToList();
        Assert.Single(hits);
        Assert.Equal("7", hits[0].Value);
    }

    [Fact]
    public void Double_Branch_Decodes_With_Invariant_Culture()
    {
        using PEFile pe = OpenConstants();
        // InvariantCulture means decimal separator is '.' not ','. If a future
        // refactor drops the explicit CultureInfo.InvariantCulture, a host
        // running under (e.g.) de-DE would produce "777,25" and this assertion
        // would fail — exactly the regression we want pinned.
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "777.25").Where(h => h.ParsedType == "Double").ToList();
        Assert.Single(hits);
        Assert.Equal("777.25", hits[0].Value);
    }

    [Fact]
    public void Single_Branch_Decodes_With_Invariant_Culture()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "777.5").Where(h => h.ParsedType == "Single").ToList();
        Assert.Single(hits);
        Assert.Equal("777.5", hits[0].Value);
    }

    // An empty search needle matches every decoded value (string.Contains("",
    // Ordinal) is always true), so this enumerates every Constant table row in
    // the fixture and pins all seven type-code branches as a unit — no branch
    // silently swallowed by the switch.
    [Fact]
    public void All_Seven_Branches_Are_Reached_By_The_Switch()
    {
        using PEFile pe = OpenConstants();
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "");
        HashSet<string> codes = new HashSet<string>(hits.Select(h => h.ParsedType));
        Assert.Contains("Int32", codes);
        Assert.Contains("Int64", codes);
        Assert.Contains("Boolean", codes);
        Assert.Contains("String", codes);
        Assert.Contains("Char", codes);
        Assert.Contains("Double", codes);
        Assert.Contains("Single", codes);
    }
}
