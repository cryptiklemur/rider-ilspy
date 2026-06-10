using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RiderIlSpy;

/// <summary>
/// Parses the SourceLink JSON blob found in a portable PDB's CustomDebugInformation
/// table (GUID cc110556-a091-4d38-9fec-25ab9a351a6a) and resolves a local document
/// path to its remote URL. Pure and testable — does no I/O.
/// </summary>
/// <remarks>
/// The SourceLink schema is documented at
/// https://github.com/dotnet/sourcelink/blob/main/docs/SourceLinkSpecification.md.
/// Shape:
/// <code>
/// {
///   "documents": {
///     "C:\\src\\repo\\*": "https://raw.githubusercontent.com/owner/repo/sha/*",
///     "/home/user/proj/*": "https://example.com/proj/*"
///   }
/// }
/// </code>
/// The wildcard <c>*</c> can appear once per pattern, on both sides. The longest
/// matching prefix wins. Mapping is purely textual — backslashes are not
/// normalised, but the matcher treats both <c>\</c> and <c>/</c> as equivalent
/// path separators so a Windows-built PDB resolves correctly on Linux.
/// </remarks>
public sealed class SourceLinkMapping
{
    private readonly IReadOnlyList<DocumentRule> myRules;

    private SourceLinkMapping(IReadOnlyList<DocumentRule> rules)
    {
        myRules = rules;
    }

    /// <summary>
    /// Parses a SourceLink JSON blob. Returns <c>null</c> when the blob is
    /// missing, malformed, or contains no usable <c>documents</c> entries.
    /// </summary>
    public static SourceLinkMapping? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("documents", out JsonElement docs)) return null;
            if (docs.ValueKind != JsonValueKind.Object) return null;
            List<DocumentRule> rules = new List<DocumentRule>();
            foreach (JsonProperty prop in docs.EnumerateObject())
            {
                string pattern = prop.Name;
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                string? replacement = prop.Value.GetString();
                if (string.IsNullOrEmpty(replacement)) continue;
                DocumentRule? rule = DocumentRule.TryCreate(pattern, replacement);
                if (rule != null) rules.Add(rule);
            }
            if (rules.Count == 0) return null;
            return new SourceLinkMapping(rules);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves <paramref name="documentPath"/> (as recorded in the PDB Document
    /// table) to the remote URL declared by SourceLink, or <c>null</c> if no
    /// rule matches. The longest prefix wins on ties.
    /// </summary>
    public string? ResolveUrl(string documentPath)
    {
        if (string.IsNullOrEmpty(documentPath)) return null;
        DocumentRule? best = null;
        int bestPrefixLen = -1;
        foreach (DocumentRule rule in myRules)
        {
            int matchLen = rule.MatchPrefixLength(documentPath);
            if (matchLen < 0) continue;
            if (matchLen > bestPrefixLen)
            {
                best = rule;
                bestPrefixLen = matchLen;
            }
        }
        return best?.BuildUrl(documentPath);
    }

    private sealed class DocumentRule
    {
        private readonly string myPrefix;
        private readonly bool myHasWildcard;
        private readonly string myReplacementPrefix;
        private readonly string myReplacementSuffix;

        private DocumentRule(string prefix, bool hasWildcard, string replacementPrefix, string replacementSuffix)
        {
            myPrefix = prefix;
            myHasWildcard = hasWildcard;
            myReplacementPrefix = replacementPrefix;
            myReplacementSuffix = replacementSuffix;
        }

        public static DocumentRule? TryCreate(string pattern, string replacement)
        {
            int starIndex = pattern.IndexOf('*');
            if (starIndex < 0)
            {
                int replStar = replacement.IndexOf('*');
                if (replStar >= 0) return null;
                return new DocumentRule(pattern, hasWildcard: false, replacement, "");
            }
            if (pattern.IndexOf('*', starIndex + 1) >= 0) return null;
            int replStarIndex = replacement.IndexOf('*');
            if (replStarIndex < 0) return null;
            if (replacement.IndexOf('*', replStarIndex + 1) >= 0) return null;
            string prefix = pattern.Substring(0, starIndex);
            string replPrefix = replacement.Substring(0, replStarIndex);
            string replSuffix = replacement.Substring(replStarIndex + 1);
            return new DocumentRule(prefix, hasWildcard: true, replPrefix, replSuffix);
        }

        public int MatchPrefixLength(string documentPath)
        {
            if (myHasWildcard)
            {
                return StartsWithIgnoringSlash(documentPath, myPrefix) ? myPrefix.Length : -1;
            }
            return string.Equals(NormalizeSlashes(documentPath), NormalizeSlashes(myPrefix), StringComparison.Ordinal)
                ? myPrefix.Length
                : -1;
        }

        public string BuildUrl(string documentPath)
        {
            if (!myHasWildcard) return myReplacementPrefix;
            string tail = documentPath.Substring(myPrefix.Length).Replace('\\', '/');
            return myReplacementPrefix + tail + myReplacementSuffix;
        }

        private static bool StartsWithIgnoringSlash(string s, string prefix)
        {
            if (s.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                char a = s[i];
                char b = prefix[i];
                if (a == b) continue;
                if ((a == '/' || a == '\\') && (b == '/' || b == '\\')) continue;
                return false;
            }
            return true;
        }

        private static string NormalizeSlashes(string s) => s.Replace('\\', '/');
    }
}
