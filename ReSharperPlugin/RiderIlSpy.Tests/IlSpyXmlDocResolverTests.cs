using Xunit;

namespace RiderIlSpy.Tests;

// Pins the xmldoc-candidate path resolver. Two concerns:
//   1. Sidecar lookup mirrors the C# compiler's "<dll>.xml next door"
//      convention — works for NuGet, Mono profile, .NETFx Reference
//      Assemblies, RimWorld mod DLLs, and our own decompile outputs.
//   2. .NET shared-runtime impl paths get redirected to the parallel ref
//      pack, where the xmldocs actually ship. The redirect target is a
//      directory (the ref/ root) — callers glob xml files because BCL
//      type forwarders break the impl-asm → xmldoc-file 1:1 mapping.
public class IlSpyXmlDocResolverTests
{
    [Fact]
    public void Sidecar_null_input_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetSidecarXmlDocPath(null));
    }

    [Fact]
    public void Sidecar_empty_input_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetSidecarXmlDocPath(""));
    }

    [Fact]
    public void Sidecar_nuget_package_path()
    {
        Assert.Equal(
            "/home/aaron/.nuget/packages/newtonsoft.json/13.0.1/lib/net6.0/Newtonsoft.Json.xml",
            IlSpyXmlDocResolver.GetSidecarXmlDocPath(
                "/home/aaron/.nuget/packages/newtonsoft.json/13.0.1/lib/net6.0/Newtonsoft.Json.dll"));
    }

    [Fact]
    public void Sidecar_mono_profile_path()
    {
        Assert.Equal(
            "/usr/lib/mono/4.5/mscorlib.xml",
            IlSpyXmlDocResolver.GetSidecarXmlDocPath("/usr/lib/mono/4.5/mscorlib.dll"));
    }

    [Fact]
    public void Sidecar_netfx_reference_assemblies_path()
    {
        Assert.Equal(
            "C:\\Program Files (x86)\\Reference Assemblies\\Microsoft\\Framework\\.NETFramework\\v4.8\\mscorlib.xml",
            IlSpyXmlDocResolver.GetSidecarXmlDocPath(
                "C:\\Program Files (x86)\\Reference Assemblies\\Microsoft\\Framework\\.NETFramework\\v4.8\\mscorlib.dll"));
    }

    [Fact]
    public void Sidecar_rimworld_mod_path()
    {
        // Sanity check: user-mod DLLs go through the same code path as any
        // other sidecar. If the mod ships an xmldoc file we'll pick it up; if
        // not, File.Exists at the call site filters it out.
        Assert.Equal(
            "/mnt/games/RimWorld/Mods/RimBridgeServer/1.6/Assemblies/RimBridgeServer.xml",
            IlSpyXmlDocResolver.GetSidecarXmlDocPath(
                "/mnt/games/RimWorld/Mods/RimBridgeServer/1.6/Assemblies/RimBridgeServer.dll"));
    }

    [Fact]
    public void RefPack_null_input_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(null));
    }

    [Fact]
    public void RefPack_empty_input_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(""));
    }

    [Fact]
    public void RefPack_linux_netcore_impl_redirects_to_parallel_ref_pack()
    {
        Assert.Equal(
            "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.4/ref",
            IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
                "/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.4/System.Private.CoreLib.dll"));
    }

    [Fact]
    public void RefPack_windows_netcore_impl_redirects_with_backslash_style()
    {
        Assert.Equal(
            "C:\\Program Files\\dotnet\\packs\\Microsoft.NETCore.App.Ref\\8.0.0\\ref",
            IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
                "C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\8.0.0\\System.Private.CoreLib.dll"));
    }

    [Fact]
    public void RefPack_aspnetcore_impl_redirects()
    {
        Assert.Equal(
            "/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.0/ref",
            IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
                "/usr/share/dotnet/shared/Microsoft.AspNetCore.App/8.0.0/Microsoft.AspNetCore.dll"));
    }

    [Fact]
    public void RefPack_windowsdesktop_impl_redirects()
    {
        Assert.Equal(
            "C:\\Program Files\\dotnet\\packs\\Microsoft.WindowsDesktop.App.Ref\\8.0.0\\ref",
            IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
                "C:\\Program Files\\dotnet\\shared\\Microsoft.WindowsDesktop.App\\8.0.0\\PresentationCore.dll"));
    }

    [Fact]
    public void RefPack_nuget_package_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
            "/home/aaron/.nuget/packages/newtonsoft.json/13.0.1/lib/net6.0/Newtonsoft.Json.dll"));
    }

    [Fact]
    public void RefPack_mono_path_returns_null()
    {
        // Mono BCL doesn't have a ref-pack convention — the sidecar at
        // /usr/lib/mono/<profile>/<asm>.xml is the only xmldoc location.
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory("/usr/lib/mono/4.5/mscorlib.dll"));
    }

    [Fact]
    public void RefPack_netfx_reference_assemblies_returns_null()
    {
        // .NETFx Reference Assemblies are themselves the xmldoc-bearing
        // surface — the sidecar covers them, no redirect needed.
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
            "C:\\Program Files (x86)\\Reference Assemblies\\Microsoft\\Framework\\.NETFramework\\v4.8\\mscorlib.dll"));
    }

    [Fact]
    public void RefPack_user_assembly_returns_null()
    {
        Assert.Null(IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(
            "/home/aaron/projects/foo/bin/Debug/net8.0/Foo.dll"));
    }
}
