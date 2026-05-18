using System.Collections.Generic;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class ResourceQueryHandlerTests
{
    [Fact]
    public void Substring_Match()
    {
        IlSpySearchIndex idx = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        idx.AddResource(new ResourceIndexEntry(asm, 1, "embedded.txt", null, 100, "text"));
        idx.AddResource(new ResourceIndexEntry(asm, 2, "logo.png", null, 5000, "image"));

        List<ResourceIndexEntry> hits = new ResourceQueryHandler(idx).Query("emb");
        Assert.Single(hits);
        Assert.Equal("embedded.txt", hits[0].ResourceName);
    }
}
