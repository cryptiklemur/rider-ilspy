using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class ConstantQueryHandlerTests
{
    [Fact]
    public void Returns_Empty_When_No_Constants_Match()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        using PEFile pe = new PEFile(path);
        List<ConstantHit> hits = new ConstantQueryHandler().Scan(new[] { pe }, "9999");
        Assert.Empty(hits);
    }
}
