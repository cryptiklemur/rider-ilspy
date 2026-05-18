namespace RiderIlSpy.Search;

public readonly record struct ResourceIndexEntry(
    AssemblyId AssemblyId,
    int ManifestResourceToken,
    string ResourceName,
    string? ParentEntryName,
    long SizeBytes,
    string MimeHint);
