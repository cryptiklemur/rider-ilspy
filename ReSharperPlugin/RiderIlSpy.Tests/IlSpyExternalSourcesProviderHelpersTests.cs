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
        // Ref-pack line is conditional and shouldn't appear for a non-shared-runtime
        // dummy path like /tmp/MyLib.dll.
        Assert.DoesNotContain("// XML documentation ref pack:", banner);
    }

    [Fact]
    public void BuildDiagnosticBanner_emits_ref_pack_row_for_dotnet_shared_runtime_path()
    {
        // Stand up a synthetic .NET shared-runtime + ref-pack layout in a
        // temp directory and assert the banner picks up the parallel ref
        // pack. Using real filesystem (rather than a mock) because the
        // banner builder is intentionally I/O-bound — it counts xml files
        // on disk to emit the "(N files)" suffix.
        string root = Path.Combine(Path.GetTempPath(), "rider-ilspy-banner-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string implDir = Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.4");
            string refTfmDir = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref", "10.0.4", "ref", "net10.0");
            Directory.CreateDirectory(implDir);
            Directory.CreateDirectory(refTfmDir);
            File.WriteAllText(Path.Combine(refTfmDir, "System.Runtime.xml"), "<doc/>");
            File.WriteAllText(Path.Combine(refTfmDir, "System.Numerics.Vectors.xml"), "<doc/>");
            string implPath = Path.Combine(implDir, "System.Private.CoreLib.dll");
            File.WriteAllText(implPath, ""); // banner reads xml count only — empty dll is fine

            BannerContext ctx = new BannerContext(Meta: null, AssemblyPath: implPath, TypeFullName: "MyNs.MyType", Mode: IlSpyOutputMode.CSharp, ExtraSearchDirs: new string[] { });
            string banner = IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(ctx, sourceLinkOutcome: null);

            Assert.Contains("// XML documentation ref pack:", banner);
            Assert.Contains("(2 files)", banner);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
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

    // InitialSourceLinkOutcome chooses the banner-visible SourceLink status
    // BEFORE the fetch runs, so the user can tell from the banner why
    // SourceLink didn't contribute. Three branches; all three pinned here.

    [Fact]
    public void InitialSourceLinkOutcome_returns_Disabled_when_preferSourceLink_is_false()
    {
        SourceLinkOutcome outcome = IlSpyExternalSourcesProviderHelpers.InitialSourceLinkOutcome(preferSourceLink: false, mode: IlSpyOutputMode.CSharp);
        Assert.Equal(SourceLinkStatus.Disabled, outcome.Status);
    }

    [Fact]
    public void InitialSourceLinkOutcome_returns_SkippedMode_for_IL_mode_even_when_preferSourceLink_is_true()
    {
        SourceLinkOutcome outcome = IlSpyExternalSourcesProviderHelpers.InitialSourceLinkOutcome(preferSourceLink: true, mode: IlSpyOutputMode.IL);
        Assert.Equal(SourceLinkStatus.SkippedMode, outcome.Status);
    }

    [Fact]
    public void InitialSourceLinkOutcome_returns_SkippedMode_for_CSharpWithIL_mode()
    {
        // CSharpWithIL is not pure C#, so SourceLink (which delivers C#-only
        // sources) doesn't apply — the choice mirrors IL-only mode.
        SourceLinkOutcome outcome = IlSpyExternalSourcesProviderHelpers.InitialSourceLinkOutcome(preferSourceLink: true, mode: IlSpyOutputMode.CSharpWithIL);
        Assert.Equal(SourceLinkStatus.SkippedMode, outcome.Status);
    }

    [Fact]
    public void InitialSourceLinkOutcome_returns_NotAttempted_when_preferSourceLink_and_CSharp_mode()
    {
        SourceLinkOutcome outcome = IlSpyExternalSourcesProviderHelpers.InitialSourceLinkOutcome(preferSourceLink: true, mode: IlSpyOutputMode.CSharp);
        Assert.Equal(SourceLinkStatus.NotAttempted, outcome.Status);
    }

    // IsRefAssemblyPath: pure heuristic for "this path points at a reference-
    // only assembly". The three accepted markers are the canonical SDK shapes.

    [Theory]
    [InlineData("/usr/share/dotnet/sdk/8.0.100/ref/Microsoft.NETCore.App.dll")]
    [InlineData("C:\\Program Files\\dotnet\\packs\\ref\\System.dll")]
    [InlineData("/home/u/.nuget/packages/runtime.linux-x64.microsoft.netcore.app/8.0.0/runtimes/linux-x64/lib/net8.0/.ref/Foo.dll")]
    public void IsRefAssemblyPath_recognises_canonical_ref_markers(string path)
    {
        Assert.True(IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(path));
    }

    [Theory]
    [InlineData("/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.0/System.Private.CoreLib.dll")]
    [InlineData("C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\8.0.0\\System.dll")]
    [InlineData("/home/u/projects/MyLib/bin/Debug/MyLib.dll")]
    public void IsRefAssemblyPath_rejects_implementation_paths(string path)
    {
        Assert.False(IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(path));
    }

    [Fact]
    public void IsRefAssemblyPath_returns_false_for_null_or_empty()
    {
        Assert.False(IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(null!));
        Assert.False(IlSpyExternalSourcesProviderHelpers.IsRefAssemblyPath(string.Empty));
    }

    // ParseExtraSearchDirs is the split-and-accumulate wrapper around
    // TryNormalizeSearchDir. Lives in the helpers class so the wiring
    // (delimiter, accumulator, warn-on-rejection) is unit-testable without
    // pulling in IlSpyRequestSettingsBuilder's IContextBoundSettingsStoreLive
    // dependency. The delimiter is Path.PathSeparator — ';' on Windows,
    // ':' on Linux/macOS — chosen to mirror each platform's PATH convention.

    [Fact]
    public void ParseExtraSearchDirs_returns_empty_for_null_raw()
    {
        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(null, warnings.Add);
        Assert.Empty(result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_returns_empty_for_empty_raw()
    {
        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs("", warnings.Add);
        Assert.Empty(result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_returns_empty_for_whitespace_raw()
    {
        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs("   ", warnings.Add);
        Assert.Empty(result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_accepts_single_existing_directory()
    {
        string tempDir = Path.GetTempPath();
        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(tempDir, warnings.Add);
        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(tempDir), result[0]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_splits_on_platform_path_separator()
    {
        // Both halves point at directories that exist (Path.GetTempPath() and
        // its parent both resolve on every platform). Joining with
        // Path.PathSeparator pins the contract: the splitter follows the
        // platform's PATH convention rather than hard-coding one char.
        string tempDir = Path.GetFullPath(Path.GetTempPath());
        string parentDir = Path.GetFullPath(Path.Combine(tempDir, ".."));
        string raw = tempDir + Path.PathSeparator + parentDir;

        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(raw, warnings.Add);
        Assert.Equal(2, result.Count);
        Assert.Equal(tempDir, result[0]);
        Assert.Equal(parentDir, result[1]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_warns_once_per_rejected_entry()
    {
        // One valid + one rejected (relative path) entry — the valid one
        // makes it through, the rejected one fires exactly one warn.
        string tempDir = Path.GetFullPath(Path.GetTempPath());
        string raw = tempDir + Path.PathSeparator + "relative/path/segment";

        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(raw, warnings.Add);
        Assert.Single(result);
        Assert.Equal(tempDir, result[0]);
        Assert.Single(warnings);
        Assert.Contains("non-absolute", warnings[0]);
    }

    [Fact]
    public void ParseExtraSearchDirs_drops_empty_segments_silently()
    {
        // Trailing/leading/duplicate separators emit empty segments that
        // RemoveEmptyEntries discards — no rejection, no warning.
        string tempDir = Path.GetFullPath(Path.GetTempPath());
        string raw = Path.PathSeparator + tempDir + Path.PathSeparator + Path.PathSeparator;

        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(raw, warnings.Add);
        Assert.Single(result);
        Assert.Equal(tempDir, result[0]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParseExtraSearchDirs_preserves_duplicate_entries()
    {
        // Pinning current behaviour: the helper does NOT de-duplicate
        // canonical paths. If a user lists the same dir twice, both copies
        // flow through to the consumer (which is fine — the resolver loop
        // just probes the same dir twice). De-dup is a future concern;
        // changing it should change this test deliberately.
        string tempDir = Path.GetFullPath(Path.GetTempPath());
        string raw = tempDir + Path.PathSeparator + tempDir;

        List<string> warnings = new List<string>();
        IReadOnlyList<string> result = IlSpyExternalSourcesProviderHelpers.ParseExtraSearchDirs(raw, warnings.Add);
        Assert.Equal(2, result.Count);
        Assert.Equal(tempDir, result[0]);
        Assert.Equal(tempDir, result[1]);
        Assert.Empty(warnings);
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
