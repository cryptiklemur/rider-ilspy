using System;
using System.IO;

namespace RiderIlSpy;

public static class EngineDirectoryResolver
{
    public const string EngineFileName = "RiderIlSpy.Engine.dll";

    /// <summary>
    /// Finds the engine directory given the directory holding RiderIlSpy.dll.
    /// Falls back to <paramref name="hostDirectory"/> itself for flat dev builds.
    /// </summary>
    public static string Resolve(string hostDirectory, Func<string, bool> fileExists)
    {
        string sibling = Path.GetFullPath(Path.Combine(hostDirectory, "..", "engine"));
        if (fileExists(Path.Combine(sibling, EngineFileName))) return sibling;
        return hostDirectory;
    }
}
