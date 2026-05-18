namespace RiderIlSpy.Search;

public readonly record struct LiteralIndexEntry(
    AssemblyId AssemblyId,
    int UserStringToken,
    int ContainingMethodToken,
    int IlOffset,
    string StringValue);
