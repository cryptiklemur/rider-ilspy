using System.Collections.Generic;

namespace RiderIlSpy.Search;

public sealed class LiteralQueryHandler
{
    private readonly IlSpySearchIndex myIndex;

    public LiteralQueryHandler(IlSpySearchIndex index) => myIndex = index;

    public List<LiteralIndexEntry> Query(LiteralQuery q)
    {
        IEnumerable<LiteralIndexEntry> candidates = CollectCandidates(q);
        TextMatcher matcher = new TextMatcher(q.Input, q.CaseSensitive, q.Regex, q.WholeWord);
        List<LiteralIndexEntry> hits = new List<LiteralIndexEntry>();
        HashSet<(AssemblyId, int)> seen = new HashSet<(AssemblyId, int)>();
        foreach (LiteralIndexEntry entry in candidates)
        {
            if (!seen.Add((entry.AssemblyId, entry.UserStringToken))) continue;
            if (matcher.Matches(entry.StringValue)) hits.Add(entry);
        }
        return hits;
    }

    private IEnumerable<LiteralIndexEntry> CollectCandidates(LiteralQuery q)
    {
        if (q.Regex || q.Input.Length < 3)
            return myIndex.AllLiteralEntries();
        // The index always stores lowercase trigrams, so always look up case-insensitively.
        // Post-filtering in Query() handles actual case sensitivity.
        HashSet<string> trigrams = TrigramExtractor.Extract(q.Input, caseSensitive: false);
        if (trigrams.Count == 0) return myIndex.AllLiteralEntries();
        List<LiteralIndexEntry>? smallest = null;
        foreach (string tg in trigrams)
        {
            List<LiteralIndexEntry> bucket = myIndex.LookupLiteralCandidatesByTrigram(tg, caseSensitive: false);
            if (smallest == null || bucket.Count < smallest.Count) smallest = bucket;
        }
        return smallest ?? new List<LiteralIndexEntry>();
    }
}
