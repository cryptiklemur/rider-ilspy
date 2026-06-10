using System;
using System.Globalization;

namespace RiderIlSpy.Search;

public static class TokenQueryHandler
{
    public static bool TryParse(string input, out int token, out string? assemblyName)
    {
        token = 0;
        assemblyName = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        string s = input.Trim();
        int hashIdx = s.IndexOf('#');
        if (hashIdx > 0)
        {
            assemblyName = s[..hashIdx];
            s = s[(hashIdx + 1)..];
        }
        else if (s.StartsWith("#", StringComparison.Ordinal))
        {
            s = s[1..];
        }
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }
        else
        {
            return false;
        }
        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out token);
    }
}
