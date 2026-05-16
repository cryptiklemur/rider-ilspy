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
        IReadOnlyDictionary<string, string> props = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(
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
        IReadOnlyDictionary<string, string> props = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(mode, "asm", "T", "m", "f.cs");
        Assert.Equal(mode.ToString(), props["RiderIlSpy.Mode"]);
    }

    // The explicit-homeDir overload exists precisely so tests don't have to
    // mutate the process-wide HOME env var — that pattern races under xunit
    // parallelism. Passing the home explicitly keeps each fact hermetic.
    [Fact]
    public void RedactHome_replaces_home_prefix_with_tilde()
    {
        string redacted = IlSpyExternalSourcesProviderHelpers.RedactHome("/test/home/projects/foo.dll", "/test/home");
        Assert.Equal("~/projects/foo.dll", redacted);
    }

    [Fact]
    public void RedactHome_leaves_unrelated_paths_unchanged()
    {
        string redacted = IlSpyExternalSourcesProviderHelpers.RedactHome("/opt/dotnet/sdk/foo.dll", "/test/home");
        Assert.Equal("/opt/dotnet/sdk/foo.dll", redacted);
    }

    [Fact]
    public void RedactHome_returns_path_when_home_is_null_or_empty()
    {
        Assert.Equal("/some/path", IlSpyExternalSourcesProviderHelpers.RedactHome("/some/path", null));
        Assert.Equal("/some/path", IlSpyExternalSourcesProviderHelpers.RedactHome("/some/path", string.Empty));
    }

    [Fact]
    public void RedactHome_returns_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, IlSpyExternalSourcesProviderHelpers.RedactHome(string.Empty));
    }

    [Fact]
    public void XmlDocPathOrEmpty_changes_extension_to_xml()
    {
        Assert.Equal("/tmp/MyLib.xml", IlSpyExternalSourcesProviderHelpers.XmlDocPathOrEmpty("/tmp/MyLib.dll"));
    }

    [Fact]
    public void XmlDocPathOrEmpty_returns_empty_for_empty_input()
    {
        Assert.Equal(string.Empty, IlSpyExternalSourcesProviderHelpers.XmlDocPathOrEmpty(string.Empty));
    }

    [Fact]
    public void GetDecompilerVersion_returns_non_empty_version()
    {
        // ICSharpCode.Decompiler 8.2 is referenced — version must resolve to
        // something non-empty (either a 3-part version or the "unknown" sentinel).
        string v = IlSpyExternalSourcesProviderHelpers.GetDecompilerVersion();
        Assert.False(string.IsNullOrEmpty(v));
    }

    private static BannerContext Ctx(AssemblyBannerMetadata? meta, IlSpyOutputMode mode = IlSpyOutputMode.CSharp, IReadOnlyList<string>? extraSearchDirs = null)
        => new BannerContext(meta, "/tmp/MyLib.dll", "MyNs.MyType", mode, extraSearchDirs ?? new string[] { });

    [Fact]
    public void WithBannerIfEnabled_returns_content_unchanged_when_disabled()
    {
        string result = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(
            showBanner: false,
            ctx: Ctx(meta: null),
            content: "namespace MyNs { class MyType {} }");
        Assert.Equal("namespace MyNs { class MyType {} }", result);
    }

    [Fact]
    public void WithBannerIfEnabled_prepends_banner_when_enabled()
    {
        string result = IlSpyExternalSourcesProviderHelpers.WithBannerIfEnabled(
            showBanner: true,
            ctx: Ctx(meta: null),
            content: "BODY");
        Assert.EndsWith("BODY", result);
        Assert.StartsWith("// Decompiled with RiderIlSpy", result);
        Assert.Contains("// Type: MyNs.MyType", result);
        Assert.Contains("// Mode: CSharp", result);
    }

    [Fact]
    public void BuildDiagnosticBanner_emits_path_and_mode_rows_when_meta_is_null()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(Ctx(meta: null, mode: IlSpyOutputMode.IL), sourceLinkOutcome: null);
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

    // SourceLink status surfacing: the 3-overload BuildDiagnosticBanner adds a
    // "// SourceLink: <status>" line when the outcome is interesting (i.e. not
    // Disabled / SkippedMode / NotAttempted, which the formatter silences).
    // These regression tests pin both the emit-when-interesting and
    // silence-when-not branches so future banner tweaks don't accidentally
    // leak "disabled" into output or drop a real "no-pdb" diagnostic on the floor.
    [Fact]
    public void BuildDiagnosticBanner_emits_sourcelink_status_when_interesting()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            Ctx(meta: null),
            sourceLinkOutcome: SourceLinkOutcome.Plain(SourceLinkStatus.NoPdb));
        Assert.Contains("// SourceLink: no-pdb", banner);
    }

    [Fact]
    public void BuildDiagnosticBanner_omits_sourcelink_status_when_disabled()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            Ctx(meta: null),
            sourceLinkOutcome: SourceLinkOutcome.Plain(SourceLinkStatus.Disabled));
        Assert.DoesNotContain("// SourceLink:", banner);
    }

    [Fact]
    public void BuildDiagnosticBanner_omits_sourcelink_status_when_skipped_for_non_csharp_mode()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            Ctx(meta: null, mode: IlSpyOutputMode.IL),
            sourceLinkOutcome: SourceLinkOutcome.Plain(SourceLinkStatus.SkippedMode));
        Assert.DoesNotContain("// SourceLink:", banner);
    }

    [Fact]
    public void BuildDiagnosticBanner_emits_sourcelink_used_url()
    {
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(
            Ctx(meta: null),
            sourceLinkOutcome: SourceLinkOutcome.UsedAt("https://raw.githubusercontent.com/foo/bar/abc/src/T.cs"));
        Assert.Contains("// SourceLink: used: https://raw.githubusercontent.com/foo/bar/abc/src/T.cs", banner);
    }

    [Fact]
    public void BuildDiagnosticBanner_null_sourcelink_outcome_omits_row()
    {
        // Banner must stay compatible for the "no SourceLink fork was attempted"
        // path (RedecompileAllEntriesAsync) — passing a null outcome should NOT
        // emit an empty "// SourceLink: " row.
        string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(Ctx(meta: null), sourceLinkOutcome: null);
        Assert.DoesNotContain("// SourceLink:", banner);
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
            Ctx(meta: meta, mode: IlSpyOutputMode.CSharpWithIL, extraSearchDirs: new string[] { "/opt/dotnet/sdk", "/usr/lib/dotnet" }),
            sourceLinkOutcome: null);
        Assert.Contains("// Assembly: MyLib, Version=1.2.3.4, Culture=neutral, PublicKeyToken=0123456789abcdef", banner);
        Assert.Contains("// MVID: 11111111-2222-3333-4444-555555555555", banner);
        Assert.Contains("// Target framework: .NETCoreApp,Version=v8.0", banner);
        Assert.Contains("// File size: 4,096 bytes", banner);
        Assert.Contains("// Mode: CSharpWithIL", banner);
        Assert.Contains("/opt/dotnet/sdk, /usr/lib/dotnet", banner);
    }

    [Fact]
    public void TryParseDecompileEntryFields_returns_null_when_properties_null()
    {
        Assert.Null(IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields(null));
    }

    [Fact]
    public void TryParseDecompileEntryFields_returns_null_when_moniker_missing()
    {
        Dictionary<string, string> props = new Dictionary<string, string>
        {
            ["RiderIlSpy.Assembly"] = "/tmp/a.dll",
            ["RiderIlSpy.Type"] = "Foo",
            ["RiderIlSpy.FileName"] = "Foo.cs",
            ["RiderIlSpy.Mode"] = "CSharp",
        };
        Assert.Null(IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields(props));
    }

    [Fact]
    public void TryParseDecompileEntryFields_returns_null_when_moniker_empty()
    {
        Dictionary<string, string> props = new Dictionary<string, string>
        {
            ["RiderIlSpy.Moniker"] = "",
            ["RiderIlSpy.Assembly"] = "/tmp/a.dll",
            ["RiderIlSpy.Type"] = "Foo",
            ["RiderIlSpy.FileName"] = "Foo.cs",
            ["RiderIlSpy.Mode"] = "CSharp",
        };
        Assert.Null(IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields(props));
    }

    [Fact]
    public void TryParseDecompileEntryFields_returns_null_when_mode_unparseable()
    {
        Dictionary<string, string> props = new Dictionary<string, string>
        {
            ["RiderIlSpy.Moniker"] = "m",
            ["RiderIlSpy.Assembly"] = "/tmp/a.dll",
            ["RiderIlSpy.Type"] = "Foo",
            ["RiderIlSpy.FileName"] = "Foo.cs",
            ["RiderIlSpy.Mode"] = "NotAMode",
        };
        Assert.Null(IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields(props));
    }

    [Fact]
    public void TryParseDecompileEntryFields_round_trips_BuildCacheProperties()
    {
        IReadOnlyDictionary<string, string> props = IlSpyExternalSourcesProviderHelpers.BuildCacheProperties(
            IlSpyOutputMode.CSharpWithIL, "/tmp/lib.dll", "Some.Type.Name", "moniker-1", "Type.cs");
        DecompileEntryFields? fields = IlSpyExternalSourcesProviderHelpers.TryParseDecompileEntryFields((IDictionary<string, string>)props);
        Assert.NotNull(fields);
        Assert.Equal("/tmp/lib.dll", fields!.AssemblyFilePath);
        Assert.Equal("Some.Type.Name", fields.TypeFullName);
        Assert.Equal("moniker-1", fields.Moniker);
        Assert.Equal("Type.cs", fields.FileName);
        Assert.Equal(IlSpyOutputMode.CSharpWithIL, fields.Mode);
    }
}
