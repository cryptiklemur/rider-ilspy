using System;
using System.IO;
using System.Threading;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexerBuildAllTests
{
    [Fact]
    public void Builds_All_Three_Fixtures()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "TestFixtures");
        string[] paths =
        [
            Path.Combine(dir, "literals.dll"),
            Path.Combine(dir, "attributes.dll"),
            Path.Combine(dir, "resources.dll"),
        ];
        IlSpyIndexBuildProgress? finalProgress = null;
        IlSpySearchIndex index = new IlSpySearchIndexer().BuildAll(paths, p => finalProgress = p, CancellationToken.None);

        Assert.NotNull(finalProgress);
        Assert.Equal(3, finalProgress!.Indexed);
        Assert.Equal(0, finalProgress.Skipped);
        Assert.NotEmpty(index.LookupLiteralCandidatesByTrigram("hel", false));
        Assert.True(index.LookupAttributesByFqn("System.ObsoleteAttribute").Count > 0);
    }
}
