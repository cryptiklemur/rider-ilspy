namespace RiderIlSpy.Search;

/// <summary>
/// One name match from a symbol scan. <paramref name="MetadataToken"/> is 0 for
/// <see cref="SymbolSearchKind.Namespace"/> hits, which have no metadata row of their own.
/// </summary>
public sealed record SymbolHit(
    AssemblyId AssemblyId,
    SymbolSearchKind Kind,
    string Name,
    string DisplayName,
    int MetadataToken);
