namespace RiderIlSpy.Search;

public readonly record struct AttributeIndexEntry(
    AssemblyId AssemblyId,
    string AttributeTypeFullName,
    string AttributeTypeShortName,
    int TargetMetadataToken,
    string TargetKind,
    string ArgsSummary);
