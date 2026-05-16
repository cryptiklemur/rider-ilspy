using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace RiderIlSpy.Tests;

public class IlSpyExternalSourcesProviderHelpersTests
{
    [Fact]
    public void TryNormalizeSearchDir_rejects_unc_paths()
    {
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir("\\\\server\\share", out string canonical, out string? rejection);
        Assert.False(ok);
        Assert.Empty(canonical);
        Assert.NotNull(rejection);
        Assert.Contains("UNC/network", rejection);
    }

    [Fact]
    public void TryNormalizeSearchDir_rejects_forward_slash_unc()
    {
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir("//server/share", out string _, out string? rejection);
        Assert.False(ok);
        Assert.Contains("UNC/network", rejection);
    }

    [Fact]
    public void TryNormalizeSearchDir_rejects_non_absolute_paths()
    {
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir("relative/path", out string _, out string? rejection);
        Assert.False(ok);
        Assert.NotNull(rejection);
        Assert.Contains("non-absolute", rejection);
    }

    [Fact]
    public void TryNormalizeSearchDir_silently_skips_empty_strings()
    {
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir("   ", out string _, out string? rejection);
        Assert.False(ok);
        Assert.Null(rejection);
    }

    [Fact]
    public void TryNormalizeSearchDir_rejects_nonexistent_directory()
    {
        string fakeAbsolute = Path.Combine(Path.GetTempPath(), "RiderIlSpy_DefinitelyDoesNotExist_" + Path.GetRandomFileName());
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir(fakeAbsolute, out string _, out string? rejection);
        Assert.False(ok);
        Assert.NotNull(rejection);
        Assert.Contains("does not exist", rejection);
    }

    [Fact]
    public void TryNormalizeSearchDir_accepts_existing_absolute_directory()
    {
        string tempDir = Path.GetTempPath();
        bool ok = IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir(tempDir, out string canonical, out string? rejection);
        Assert.True(ok);
        Assert.Null(rejection);
        Assert.Equal(Path.GetFullPath(tempDir), canonical);
    }

    [Fact]
    public void BuildCacheProperties_contains_all_required_keys()
    {
        IDictionary<string, string> props = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(
            IlSpyOutputMode.CSharpWithIL,
            "/tmp/MyLib.dll",
            "MyNamespace.MyType",
            "moniker-123",
            "MyType.cs");

        Assert.Equal("CSharpWithIL", props["RiderIlSpy.Mode"]);
        Assert.Equal("/tmp/MyLib.dll", props["RiderIlSpy.Assembly"]);
        Assert.Equal("MyNamespace.MyType", props["RiderIlSpy.Type"]);
        Assert.Equal("moniker-123", props["RiderIlSpy.Moniker"]);
        Assert.Equal("MyType.cs", props["RiderIlSpy.FileName"]);
    }

    [Theory]
    [InlineData(IlSpyOutputMode.CSharp)]
    [InlineData(IlSpyOutputMode.IL)]
    [InlineData(IlSpyOutputMode.CSharpWithIL)]
    public void BuildCacheProperties_encodes_mode_as_enum_member_name(IlSpyOutputMode mode)
    {
        IDictionary<string, string> props = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, "asm", "T", "m", "f.cs");
        Assert.Equal(mode.ToString(), props["RiderIlSpy.Mode"]);
    }

    [Fact]
    public void RedactHome_replaces_home_prefix_with_tilde()
    {
        string? previous = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", "/test/home");
            string redacted = IlSpyExternalSourcesProviderHelpers.RedactHome("/test/home/projects/foo.dll");
            Assert.Equal("~/projects/foo.dll", redacted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previous);
        }
    }

    [Fact]
    public void RedactHome_leaves_unrelated_paths_unchanged()
    {
        string? previous = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Environment.SetEnvironmentVariable("HOME", "/test/home");
            string redacted = IlSpyExternalSourcesProviderHelpers.RedactHome("/opt/dotnet/sdk/foo.dll");
            Assert.Equal("/opt/dotnet/sdk/foo.dll", redacted);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previous);
        }
    }

    [Fact]
    public void RedactHome_returns_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, IlSpyExternalSourcesProviderHelpers.RedactHome(string.Empty));
    }

    [Fact]
    public void SafeXmlDocPath_changes_extension_to_xml()
    {
        Assert.Equal("/tmp/MyLib.xml", IlSpyExternalSourcesProviderHelpers.SafeXmlDocPath("/tmp/MyLib.dll"));
    }

    [Fact]
    public void SafeXmlDocPath_returns_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, IlSpyExternalSourcesProviderHelpers.SafeXmlDocPath(string.Empty));
    }

    [Fact]
    public void GetDecompilerVersion_returns_non_empty_version()
    {
        // ICSharpCode.Decompiler 8.2 is referenced — version must resolve to
        // something non-empty (either a 3-part version or the "unknown" sentinel).
        string v = IlSpyExternalSourcesProviderHelpers.GetDecompilerVersion();
        Assert.False(string.IsNullOrEmpty(v));
    }

    [Fact]
    public void WithBannerIfEnabled_returns_content_unchanged_when_disabled()
    {
        string result = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(
            showBanner: false,
            meta: null,
            assemblyPath: "/tmp/MyLib.dll",
            typeFullName: "MyNs.MyType",
            mode: IlSpyOutputMode.CSharp,
            extraSearchDirs: new string[] { },
            content: "namespace MyNs { class MyType {} }");
        Assert.Equal("namespace MyNs { class MyType {} }", result);
    }

    [Fact]
    public void WithBannerIfEnabled_prepends_banner_when_enabled()
    {
        string result = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(
            showBanner: true,
            meta: null,
            assemblyPath: "/tmp/MyLib.dll",
            typeFullName: "MyNs.MyType",
            mode: IlSpyOutputMode.CSharp,
            extraSearchDirs: new string[] { },
            content: "BODY");
        Assert.EndsWith("BODY", result);
        Assert.StartsWith("// Decompiled with RiderIlSpy", result);
        Assert.Contains("// Type: MyNs.MyType", result);
        Assert.Contains("// Mode: CSharp", result);
    }

    [Fact]
    public void BuildDiagnosticBanner_emits_path_and_mode_rows_when_meta_is_null()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            meta: null,
            assemblyPath: "/tmp/MyLib.dll",
            typeFullName: "MyNs.MyType",
            mode: IlSpyOutputMode.IL,
            extraSearchDirs: new string[] { });
        Assert.Contains("// Type: MyNs.MyType", banner);
        Assert.Contains("// Mode: IL", banner);
        Assert.Contains("// Assembly location:", banner);
        Assert.Contains("// XML documentation location:", banner);
        Assert.Contains("// Extra search dirs: (none)", banner);
        Assert.DoesNotContain("// Assembly:", banner); // meta-only row is absent when meta == null
    }

    // ReadAssemblyBannerMetadata regression: previously untested in unit pipeline.
    // Crashed inside Rider with MissingMethodException after the ICSharpCode.Decompiler
    // 8.2 → 10.0 bump because the compiler had inlined a 4-arg `new PEFile(...,
    // MetadataStringDecoder)` call — 10.x added that optional 4th parameter as a
    // default, baking the longer signature into our IL, while Rider 2026.1's
    // bundled 8.2.x assembly only has the 3-arg form.
    //
    // These tests run the helper end-to-end against a real PE so any future
    // recurrence — accidental signature drift in helper code, or a dep bump that
    // re-introduces an inlined ctor that's absent from Rider's runtime — fails
    // the test pipeline locally instead of waiting for a sandboxed Rider crash.

    [Fact]
    public void ReadAssemblyBannerMetadata_succeeds_for_real_managed_assembly()
    {
        string asmPath = typeof(IlSpyExternalSourcesProviderHelpersTests).Assembly.Location;
        Assert.True(File.Exists(asmPath));

        AssemblyBannerMetadata? meta = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(asmPath);

        Assert.NotNull(meta);
        Assert.False(string.IsNullOrEmpty(meta!.Name));
        Assert.False(string.IsNullOrEmpty(meta.Version));
        Assert.Equal(36, meta.Mvid.Length); // Guid "D" format = 36 chars (32 hex + 4 dashes)
        Assert.True(meta.FileSize > 0, "file size must reflect on-disk length");
        // Don't pin Name/Version exactly — those are set by the test SDK and shift
        // across SDK versions. The non-empty + structural assertions are what's
        // load-bearing for the MissingMethodException regression.
    }

    [Fact]
    public void ReadAssemblyBannerMetadata_returns_null_for_nonexistent_path()
    {
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-banner-" + Guid.NewGuid().ToString("N") + ".dll");
        Assert.Null(IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(fake));
    }

    [Fact]
    public void ReadAssemblyBannerMetadata_returns_null_for_non_pe_file()
    {
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-nonpe-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllText(fake, "definitely not a PE");
        try
        {
            Assert.Null(IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(fake));
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public void BuildDiagnosticBanner_emits_full_metadata_when_meta_is_present()
    {
        AssemblyBannerMetadata meta = new AssemblyBannerMetadata(
            Name: "MyLib",
            Version: "1.2.3.4",
            Culture: "neutral",
            PublicKeyToken: "0123456789abcdef",
            Mvid: "11111111-2222-3333-4444-555555555555",
            FileSize: 4096,
            TargetFramework: ".NETCoreApp,Version=v8.0");
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            meta: meta,
            assemblyPath: "/tmp/MyLib.dll",
            typeFullName: "MyNs.MyType",
            mode: IlSpyOutputMode.CSharpWithIL,
            extraSearchDirs: new string[] { "/opt/dotnet/sdk", "/usr/lib/dotnet" });
        Assert.Contains("// Assembly: MyLib, Version=1.2.3.4, Culture=neutral, PublicKeyToken=0123456789abcdef", banner);
        Assert.Contains("// MVID: 11111111-2222-3333-4444-555555555555", banner);
        Assert.Contains("// Target framework: .NETCoreApp,Version=v8.0", banner);
        Assert.Contains("// File size: 4,096 bytes", banner);
        Assert.Contains("// Mode: CSharpWithIL", banner);
        Assert.Contains("/opt/dotnet/sdk, /usr/lib/dotnet", banner);
    }
}
