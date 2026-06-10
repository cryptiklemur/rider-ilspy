using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RiderIlSpy.Search;

public sealed class LiteralQueryHandler
{
    private readonly IlSpySearchIndex myIndex;

    public LiteralQueryHandler(IlSpySearchIndex index) => myIndex = index;

    public List<LiteralIndexEntry> Query(LiteralQuery q)
    {
        IEnumerable<LiteralIndexEntry> candidates = CollectCandidates(q);
        Regex? rx = q.Regex
            ? new Regex(q.Input, q.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
            : null;
        List<LiteralIndexEntry> hits = new List<LiteralIndexEntry>();
        HashSet<(AssemblyId, int)> seen = new HashSet<(AssemblyId, int)>();
        foreach (LiteralIndexEntry entry in candidates)
        {
            if (!seen.Add((entry.AssemblyId, entry.UserStringToken))) continue;
            string value = entry.StringValue;
            bool matched = rx != null
                ? rx.IsMatch(value)
                : value.IndexOf(q.Input, q.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
            if (q.WholeWord && matched && rx == null)
            {
                matched = Regex.IsMatch(value, @"\b" + Regex.Escape(q.Input) + @"\b",
                    q.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            }
            if (matched) hits.Add(entry);
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
