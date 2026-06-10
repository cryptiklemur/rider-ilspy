using System;
using System.Collections.Generic;

namespace RiderIlSpy.Search;

public sealed class ResourceQueryHandler
{
    private readonly IlSpySearchIndex myIndex;

    public ResourceQueryHandler(IlSpySearchIndex index) => myIndex = index;

    public List<ResourceIndexEntry> Query(string input)
    {
        HashSet<string> trigrams = TrigramExtractor.Extract(input, caseSensitive: false);
        List<ResourceIndexEntry> hits = new List<ResourceIndexEntry>();
        HashSet<(AssemblyId, int, string?)> seen = new HashSet<(AssemblyId, int, string?)>();

        IEnumerable<ResourceIndexEntry> source;
        if (trigrams.Count == 0)
        {
            source = myIndex.AllResourceEntries();
        }
        else
        {
            string firstTrigram = string.Empty;
            foreach (string tg in trigrams) { firstTrigram = tg; break; }
            source = myIndex.LookupResourceCandidatesByTrigram(firstTrigram);
        }

        foreach (ResourceIndexEntry entry in source)
            if (entry.ResourceName.Contains(input, StringComparison.OrdinalIgnoreCase)
                && seen.Add((entry.AssemblyId, entry.ManifestResourceToken, entry.ParentEntryName)))
                hits.Add(entry);

        return hits;
    }
}
