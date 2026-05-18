using System;
using System.IO;
using System.Collections.Generic;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexerAttributesTests
{
    [Fact]
    public void Indexes_Obsolete_Usages()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "attributes.dll");
        using PEFile pe = new PEFile(path);
        IlSpySearchIndex index = new IlSpySearchIndex();
        new IlSpySearchIndexer().IndexAttributes(pe, AssemblyMetadata.From(path), index);

        List<AttributeIndexEntry> hits = index.LookupAttributesByFqn("System.ObsoleteAttribute");
        Assert.True(hits.Count >= 3, "OldType + OldField + OldMethod should each have [Obsolete]");
    }
}
