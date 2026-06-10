using System;
using System.Linq;
using Xunit;
using DecompilerLanguageVersion = ICSharpCode.Decompiler.CSharp.LanguageVersion;

namespace RiderIlSpy.Tests;

// IlSpySettings persists IlSpyLanguageVersion numerically and the engine casts
// it straight into ICSharpCode.Decompiler's LanguageVersion
// (IlSpyEngine.ToDecompilerSettings). These tests pin that mirror: a plugin
// enum member whose value doesn't exist engine-side would silently decompile
// with the wrong feature set.
public class IlSpyLanguageVersionMirrorTests
{
    [Fact]
    public void Every_plugin_version_except_latest_exists_in_decompiler_enum_with_equal_value()
    {
        foreach (IlSpyLanguageVersion version in Enum.GetValues<IlSpyLanguageVersion>().Where(v => v != IlSpyLanguageVersion.Latest))
        {
            DecompilerLanguageVersion mapped = (DecompilerLanguageVersion)(int)version;
            Assert.True(Enum.IsDefined(mapped), $"{version} ({(int)version}) is not defined in ICSharpCode.Decompiler.CSharp.LanguageVersion");
            Assert.Equal(version.ToString(), mapped.ToString());
        }
    }

    // Regression guard for the C# 12/13/14 addition: the whole point of
    // shipping our own decompiler (instead of Rider's bundled 8.2, which
    // stops at CSharp11_0) is that these three targets exist and are real.
    [Theory]
    [InlineData(IlSpyLanguageVersion.CSharp12_0, 1200)]
    [InlineData(IlSpyLanguageVersion.CSharp13_0, 1300)]
    [InlineData(IlSpyLanguageVersion.CSharp14_0, 1400)]
    public void Modern_versions_are_present_and_numerically_aligned(IlSpyLanguageVersion version, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)version);
        Assert.True(Enum.IsDefined((DecompilerLanguageVersion)expectedValue));
    }
}
