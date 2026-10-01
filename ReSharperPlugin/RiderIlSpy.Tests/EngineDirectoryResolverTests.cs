using System.Collections.Generic;
using System.IO;
using Xunit;

namespace RiderIlSpy.Tests;

public class EngineDirectoryResolverTests
{
    private const string PluginRoot = "/plugins/ILSpy-Integration";
    private const string HostDir = PluginRoot + "/dotnet";

    private static string Resolve(params string[] existingFiles)
    {
        HashSet<string> present = new HashSet<string>(existingFiles);
        return EngineDirectoryResolver.Resolve(HostDir, present.Contains);
    }

    [Fact]
    public void Resolves_Engine_Outside_The_Dotnet_Folder_ReSharper_Scans()
    {
        string resolved = Resolve(PluginRoot + "/engine/" + EngineDirectoryResolver.EngineFileName);

        Assert.Equal(Path.GetFullPath(PluginRoot + "/engine"), resolved);
        Assert.DoesNotContain(Path.GetFullPath(HostDir), resolved);
    }

    [Fact]
    public void Prefers_The_Sibling_Engine_Over_One_Nested_Under_Dotnet()
    {
        string resolved = Resolve(
            PluginRoot + "/engine/" + EngineDirectoryResolver.EngineFileName,
            HostDir + "/engine/" + EngineDirectoryResolver.EngineFileName);

        Assert.Equal(Path.GetFullPath(PluginRoot + "/engine"), resolved);
    }

    [Fact]
    public void Falls_Back_To_Host_Directory_For_Flat_Dev_Builds()
    {
        Assert.Equal(HostDir, Resolve(HostDir + "/" + EngineDirectoryResolver.EngineFileName));
    }

    [Fact]
    public void Falls_Back_To_Host_Directory_When_Nothing_Is_Found()
    {
        Assert.Equal(HostDir, Resolve());
    }
}
