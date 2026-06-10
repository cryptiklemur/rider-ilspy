namespace RiderIlSpy.Search;

public readonly record struct LiteralQuery(string Input, bool CaseSensitive, bool Regex, bool WholeWord);
