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
        idx.AddLiteral(new LiteralIndexEntry(asm, 4, 0x06_000_004, 0, "cat sat on the mat"));
        idx.AddLiteral(new LiteralIndexEntry(asm, 5, 0x06_000_005, 0, "category of category"));
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

    // Pins the WholeWord regex-fallback branch at LiteralQueryHandler.cs:28-32:
    // when WholeWord is set and rx == null and substring matched, the result is
    // re-checked via \b<input>\b. Substring "cat" matches both "cat sat" and
    // "category" — WholeWord should drop the "category" hits while keeping
    // "cat" in "cat sat on the mat".
    [Fact]
    public void WholeWord_Excludes_Subword_Hits()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> substring = handler.Query(new LiteralQuery("cat", false, false, WholeWord: false));
        Assert.Equal(2, substring.Count);
        List<LiteralIndexEntry> wholeWord = handler.Query(new LiteralQuery("cat", false, false, WholeWord: true));
        Assert.Single(wholeWord);
        Assert.Equal(4, wholeWord[0].UserStringToken & 0xFF);
    }

    // Same case but with CaseSensitive: confirms the case flag flows through
    // to the regex fallback (RegexOptions.None when case-sensitive).
    [Fact]
    public void WholeWord_Honors_Case_Sensitivity()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> hits = handler.Query(new LiteralQuery("CAT", CaseSensitive: true, Regex: false, WholeWord: true));
        Assert.Empty(hits);
        List<LiteralIndexEntry> hitsCi = handler.Query(new LiteralQuery("CAT", CaseSensitive: false, Regex: false, WholeWord: true));
        Assert.Single(hitsCi);
    }

    // Regex + WholeWord: the guard `rx == null` on line 28 means WholeWord is
    // silently bypassed when Regex is on. Pinning this so a future change to
    // honor WholeWord under regex doesn't slip in without an explicit decision.
    [Fact]
    public void WholeWord_Is_Bypassed_When_Regex_Is_On()
    {
        LiteralQueryHandler handler = new LiteralQueryHandler(Populate());
        List<LiteralIndexEntry> hits = handler.Query(new LiteralQuery("cat", false, Regex: true, WholeWord: true));
        Assert.Equal(2, hits.Count);
    }
}
