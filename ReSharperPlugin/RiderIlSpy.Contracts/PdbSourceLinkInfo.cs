namespace RiderIlSpy;

/// <summary>
/// The two PDB facts the SourceLink gateway needs, read engine-side (the PDB
/// parser uses System.Reflection.Metadata) and carried across the boundary as
/// plain strings. A null instance means "no PDB found" — distinct from an
/// instance with null members, which means "PDB present but missing that entry".
/// </summary>
/// <param name="SourceLinkJson">Raw SourceLink JSON from the PDB's
/// CustomDebugInformation, or null when the PDB has no SourceLink entry.</param>
/// <param name="PrimaryDocumentPath">Original document path recorded in the PDB
/// for the requested type, or null when the type has no document.</param>
public sealed record PdbSourceLinkInfo(
    string? SourceLinkJson,
    string? PrimaryDocumentPath);
