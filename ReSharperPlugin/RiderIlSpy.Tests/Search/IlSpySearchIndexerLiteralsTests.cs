using System;
using System.IO;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexerLiteralsTests
{
    [Fact]
    public void Indexes_String_Literals_From_Fixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        using PEFile pe = new PEFile(path);
        IlSpySearchIndex index = new IlSpySearchIndex();
        IlSpySearchIndexer indexer = new IlSpySearchIndexer();
        indexer.IndexLiterals(pe, AssemblyMetadata.From(path), index);

        System.Collections.Generic.List<LiteralIndexEntry> hits =
            index.LookupLiteralCandidatesByTrigram("hel", caseSensitive: false);
        Assert.NotEmpty(hits);
        Assert.True(hits.Exists(e => e.StringValue.Contains("Hello, world.")),
            "literal 'Hello, world.' should produce trigrams including 'hel'");
    }
}
