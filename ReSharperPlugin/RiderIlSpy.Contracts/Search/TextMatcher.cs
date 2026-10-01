using System;
using System.Text.RegularExpressions;

namespace RiderIlSpy.Search;

/// <summary>Applies one query's regex / case / whole-word flags to candidate strings.</summary>
public sealed class TextMatcher
{
    private readonly string myInput;
    private readonly StringComparison myComparison;
    private readonly Regex? myRegex;
    private readonly Regex? myWordRegex;

    public TextMatcher(string input, bool caseSensitive, bool regex, bool wholeWord)
    {
        myInput = input;
        myComparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        RegexOptions options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        if (regex)
            myRegex = new Regex(input, options);
        else if (wholeWord)
            myWordRegex = new Regex(@"\b" + Regex.Escape(input) + @"\b", options);
    }

    public bool Matches(string value)
    {
        if (myRegex != null) return myRegex.IsMatch(value);
        if (value.IndexOf(myInput, myComparison) < 0) return false;
        return myWordRegex == null || myWordRegex.IsMatch(value);
    }
}
