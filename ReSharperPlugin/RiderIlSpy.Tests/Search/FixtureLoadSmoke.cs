using System;
using System.IO;
using ICSharpCode.Decompiler.Metadata;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class FixtureLoadSmoke
{
    [Fact]
    public void Literals_Fixture_Loads()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        using PEFile pe = new PEFile(path);
        Assert.Contains("literals", pe.Name);
    }
}
