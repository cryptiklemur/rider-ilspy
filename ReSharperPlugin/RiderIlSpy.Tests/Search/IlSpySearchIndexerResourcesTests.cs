using System;
using System.IO;
using System.Collections.Generic;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexerResourcesTests
{
    [Fact]
    public void Indexes_Embedded_Resources()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "resources.dll");
        using PEFile pe = new PEFile(path);
        IlSpySearchIndex index = new IlSpySearchIndex();
        new IlSpySearchIndexer().IndexResources(pe, AssemblyMetadata.From(path), index);

        List<ResourceIndexEntry> hits = index.LookupResourceCandidatesByTrigram("emb");
        Assert.True(hits.Exists(r => r.ResourceName.Contains("embedded")),
            "resource named with 'embedded' should produce trigrams including 'emb'");
    }
}
