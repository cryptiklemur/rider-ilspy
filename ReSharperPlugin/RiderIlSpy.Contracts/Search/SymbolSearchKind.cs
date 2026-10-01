using System;

namespace RiderIlSpy.Search;

/// <summary>Which metadata tables a symbol scan looks at. Combine with <c>|</c>.</summary>
[Flags]
public enum SymbolSearchKind
{
    None = 0,
    Type = 1,
    Method = 2,
    Field = 4,
    Property = 8,
    Event = 16,
    Namespace = 32,
    Member = Method | Field | Property | Event,
    TypeAndMember = Type | Member,
}
