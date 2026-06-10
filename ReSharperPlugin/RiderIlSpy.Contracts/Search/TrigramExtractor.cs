using System;
using System.Collections.Generic;

namespace RiderIlSpy.Search;

public static class TrigramExtractor
{
    public static HashSet<string> Extract(string input, bool caseSensitive)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        if (input.Length < 3) return result;
        string source = caseSensitive ? input : input.ToLowerInvariant();
        for (int i = 0; i <= source.Length - 3; i++)
        {
            result.Add(source.Substring(i, 3));
        }
        return result;
    }
}
