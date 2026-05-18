using System.Collections.Generic;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexTests
{
    [Fact]
    public void Add_And_Lookup_Literal_By_Trigram()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 0x70_000_001, 0x06_000_001, 0, "hello"));

        List<LiteralIndexEntry> hits = index.LookupLiteralCandidatesByTrigram("hel", caseSensitive: false);
        Assert.Equal(1, hits.Count);
        Assert.Equal("hello", hits[0].StringValue);
    }

    [Fact]
    public void Drop_Removes_All_Entries_For_Assembly()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 0, 0, 0, "alpha"));
        index.DropAssembly(asm);
        Assert.Empty(index.LookupLiteralCandidatesByTrigram("alp", caseSensitive: false));
    }
}
