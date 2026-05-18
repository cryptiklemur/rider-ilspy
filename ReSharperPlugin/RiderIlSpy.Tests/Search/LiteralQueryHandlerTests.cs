using System.Collections.Generic;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class LiteralQueryHandlerTests
{
    private static IlSpySearchIndex Populate()
    {
        IlSpySearchIndex idx = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        idx.AddLiteral(new LiteralIndexEntry(asm, 1, 0x06_000_001, 0, "Cannot serialize"));
        idx.AddLiteral(new LiteralIndexEntry(asm, 2, 0x06_000_002, 0, "Cannot deserialize"));
        idx.AddLiteral(new LiteralIndexEntry(asm, 3, 0x06_000_003, 0, "alpha beta gamma"));
        return idx;
    }

    [Fact]
    public void Substring_Match()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> hits = handler.Query(new LiteralQuery("seria", false, false, false));
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Case_Sensitive_Match()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> hits = handler.Query(new LiteralQuery("Cannot", true, false, false));
        Assert.Equal(2, hits.Count);
        List<LiteralIndexEntry> noHits = handler.Query(new LiteralQuery("cannot", true, false, false));
        Assert.Empty(noHits);
    }

    [Fact]
    public void Regex_Match()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> hits = handler.Query(new LiteralQuery(@"alpha\s+beta", false, true, false));
        Assert.Single(hits);
    }
}
