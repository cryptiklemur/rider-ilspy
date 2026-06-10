using System;
using System.IO;

namespace RiderIlSpy.Search;

public sealed record AssemblyMetadata(
    AssemblyId Id,
    string DisplayPath,
    DateTime LastWriteTimeUtc,
    long FileSize)
{
    public static AssemblyMetadata From(string path)
    {
        FileInfo fi = new FileInfo(path);
        return new AssemblyMetadata(AssemblyId.From(path), path, fi.LastWriteTimeUtc, fi.Length);
    }
}
