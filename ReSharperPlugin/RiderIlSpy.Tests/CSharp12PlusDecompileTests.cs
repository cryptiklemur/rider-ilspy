using Xunit;

namespace RiderIlSpy.Tests;

// C# 12 class-primary-constructor fixture. The decompiler can only re-emit
// the `class Foo(int bar)` shape when targeting C# >= 12 — under a C# 11
// target it must fall back to an explicit constructor. This is the
// user-visible payoff of shipping decompiler 10.x, so it gets pinned here.
public class PrimaryCtorFixture(int seed, string label)
{
    private readonly int mySeed = seed;
    public string Label { get; } = label;
    public int Next() => mySeed + 1;
}

public class CSharp12PlusDecompileTests
{
    private const string FixtureTypeName = "RiderIlSpy.Tests.PrimaryCtorFixture";
    private static string TestAssemblyPath => typeof(CSharp12PlusDecompileTests).Assembly.Location;

    private static IlSpyDecompilerOptions OptionsFor(IlSpyLanguageVersion version) => new(
        ThrowOnAssemblyResolveErrors: false,
        AsyncAwait: true,
        ExpressionBodies: true,
        NamedArguments: true,
        ShowXmlDocumentation: false,
        RemoveDeadCode: false,
        UsePrimaryConstructorSyntax: true,
        LanguageVersion: version);

    private static string Decompile(IlSpyLanguageVersion version)
    {
        IlSpyEngine engine = new IlSpyEngine();
        DecompileResult result = engine.DecompileType(TestAssemblyPath, FixtureTypeName, OptionsFor(version));
        Assert.True(result.Success, result.FailureReason);
        return result.Content;
    }

    [Theory]
    [InlineData(IlSpyLanguageVersion.CSharp12_0)]
    [InlineData(IlSpyLanguageVersion.CSharp13_0)]
    [InlineData(IlSpyLanguageVersion.CSharp14_0)]
    public void Targeting_csharp12_or_newer_reemits_class_primary_constructor(IlSpyLanguageVersion version)
    {
        string output = Decompile(version);
        Assert.Contains("class PrimaryCtorFixture(int seed, string label)", output);
    }

    [Fact]
    public void Targeting_csharp11_downgrades_primary_constructor_to_explicit_ctor()
    {
        string output = Decompile(IlSpyLanguageVersion.CSharp11_0);
        Assert.DoesNotContain("class PrimaryCtorFixture(int seed, string label)", output);
        Assert.Contains("public PrimaryCtorFixture(int seed, string label)", output);
    }
}
