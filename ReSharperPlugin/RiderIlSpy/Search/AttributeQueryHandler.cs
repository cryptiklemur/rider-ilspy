using System.Collections.Generic;

namespace RiderIlSpy.Search;

public sealed class AttributeQueryHandler
{
    private readonly IlSpySearchIndex myIndex;

    public AttributeQueryHandler(IlSpySearchIndex index) => myIndex = index;

    public List<AttributeIndexEntry> Query(string input)
    {
        List<string> candidates = new List<string>();
        candidates.Add(input);
        if (!input.EndsWith("Attribute")) candidates.Add(input + "Attribute");
        if (input.EndsWith("Attribute")) candidates.Add(input[..^9]);

        List<AttributeIndexEntry> results = new List<AttributeIndexEntry>();
        HashSet<string> seen = new HashSet<string>();
        foreach (string c in candidates)
        {
            foreach (AttributeIndexEntry entry in myIndex.LookupAttributesByFqn(c))
                if (seen.Add(entry.AttributeTypeFullName + "/" + entry.TargetMetadataToken))
                    results.Add(entry);
            foreach (AttributeIndexEntry entry in myIndex.LookupAttributesByShortName(c))
                if (seen.Add(entry.AttributeTypeFullName + "/" + entry.TargetMetadataToken))
                    results.Add(entry);
        }
        return results;
    }
}
