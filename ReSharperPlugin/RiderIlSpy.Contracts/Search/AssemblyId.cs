using System;
using System.IO;

namespace RiderIlSpy.Search;

/// <summary>
/// Identifier for an indexed assembly. <see cref="NormalizedPath"/> is the
/// full, original-case path (so navigation can <c>File.Exists</c> it on
/// case-sensitive filesystems). Equality and hashing are ordinal-ignore-case
/// so two paths that point at the same file on Windows / case-insensitive
/// mounts still dedup.
/// </summary>
public readonly record struct AssemblyId
{
    public string NormalizedPath { get; }

    public AssemblyId(string normalizedPath)
    {
        NormalizedPath = normalizedPath;
    }

    public static AssemblyId From(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
        return new AssemblyId(Path.GetFullPath(path));
    }

    public bool Equals(AssemblyId other) =>
        string.Equals(NormalizedPath, other.NormalizedPath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        NormalizedPath == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedPath);
}
