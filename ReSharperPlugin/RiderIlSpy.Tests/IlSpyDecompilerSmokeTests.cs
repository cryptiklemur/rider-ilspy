using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using Xunit;

namespace RiderIlSpy.Tests;

// Fixture for the record-struct navigation regression test. ILSpy decompiles
// this back to source with either positional-ctor syntax (UsePrimaryConstructorSyntax = true)
// or explicit property declarations (UsePrimaryConstructorSyntax = false).
// The latter is what Rider needs to ctrl-click to a parameter site.
public record struct BugTwoRecordFixture(int Bar, string Baz);

// Smoke tests that exercise the IlSpyDecompiler orchestration end-to-end
// against a real assembly. Use the test assembly itself as the fixture so
// we don't need to commit a binary blob to the repo.
//
// These cover the three IlSpyOutputMode branches in DecompileType plus the
// per-mode entry points (DecompileToCSharp / DisassembleToIl / DisassembleMixed)
// against the same input, which is the minimum needed to catch a rolling
// 2026.2 EAP API drift in the decompiler surface before it ships.
public class IlSpyDecompilerSmokeTests
{
    private static string TestAssemblyPath => typeof(IlSpyDecompilerSmokeTests).Assembly.Location;
    private const string FixtureTypeName = "RiderIlSpy.Tests.IlSpyDecompilerSmokeTests";

    [Fact]
    public void DecompileType_csharp_mode_emits_recognizable_source()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        string output = decompiler.DecompileType(TestAssemblyPath, FixtureTypeName, settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        Assert.False(string.IsNullOrEmpty(output));
        Assert.Contains("IlSpyDecompilerSmokeTests", output);
        Assert.Contains("class", output); // class keyword present
    }

    [Fact]
    public void DecompileType_il_mode_emits_il_opcode_markers()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        string output = decompiler.DecompileType(TestAssemblyPath, FixtureTypeName, settings, extraSearchDirs: null, mode: IlSpyOutputMode.IL);
        Assert.False(string.IsNullOrEmpty(output));
        // ILSpy IL disassembler emits ".method", "IL_xxxx:" offset markers, and
        // ".class" headers — any of these is sufficient to confirm we're in IL mode.
        Assert.True(output.Contains(".method") || output.Contains(".class") || output.Contains("IL_"));
    }

    [Fact]
    public void DecompileType_mixed_mode_emits_both_csharp_and_il()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        string output = decompiler.DecompileType(TestAssemblyPath, FixtureTypeName, settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharpWithIL);
        Assert.False(string.IsNullOrEmpty(output));
        // Mixed mode runs MixedMethodBodyDisassembler — output should carry both
        // C# fragments AND IL-style markers ("IL_" offset labels or ldarg/ret opcodes).
        Assert.Contains("IlSpyDecompilerSmokeTests", output);
        Assert.True(output.Contains("IL_") || output.Contains("ldarg") || output.Contains("ret"));
    }

    [Fact]
    public void DecompileType_unknown_type_returns_empty_or_marker_string()
    {
        // Negative path: a type that does not exist in the assembly. ILSpy
        // generally emits nothing or a synthetic marker rather than crashing.
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        string output = decompiler.DecompileType(TestAssemblyPath, "RiderIlSpy.Tests.DefinitelyDoesNotExist", settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        // We don't assert on exact contents — just that the call returns
        // without throwing and produces a string.
        Assert.NotNull(output);
    }

    // These tests target the pure helper (IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata)
    // rather than IlSpyDecompiler.GetAssemblyBannerMetadata because the latter references
    // ourLogger, whose JetBrains.Util.Logging dep won't JIT-verify in the xunit harness.
    // The wrapper on IlSpyDecompiler is a 3-line delegation + Warn — its only behavior
    // not covered here is the warn-on-null log path, which is non-essential to verify.
    [Fact]
    public void ReadAssemblyBannerMetadata_returns_metadata_for_test_assembly()
    {
        AssemblyBannerMetadata? meta = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(TestAssemblyPath);
        Assert.NotNull(meta);
        Assert.False(string.IsNullOrEmpty(meta!.Name));
        Assert.False(string.IsNullOrEmpty(meta.Version));
        // FileSize must be the real on-disk length, not zero.
        Assert.Equal(new FileInfo(TestAssemblyPath).Length, meta.FileSize);
    }

    [Fact]
    public void ReadAssemblyBannerMetadata_returns_null_for_missing_path()
    {
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpy_DefinitelyMissing_" + Path.GetRandomFileName() + ".dll");
        AssemblyBannerMetadata? meta = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(fake);
        Assert.Null(meta);
    }

    // Regression for: ctrl-click on a field defined in a record struct's
    // primary constructor lands at the top of the decompiled file rather
    // than at the parameter site. The fix exposes UsePrimaryConstructorSyntax
    // (default false) so ILSpy emits explicit property declarations whose
    // definition sites Rider's navigation can resolve. This test pins the
    // contract: with the flag off, properties must appear as bodied
    // declarations rather than only as positional ctor parameters.
    [Fact]
    public void DecompileType_record_struct_with_primary_ctor_disabled_emits_property_declarations()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings
        {
            UsePrimaryConstructorSyntax = false,
        };
        string output = decompiler.DecompileType(TestAssemblyPath, "RiderIlSpy.Tests.BugTwoRecordFixture", settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        Assert.False(string.IsNullOrEmpty(output));
        // With primary ctor syntax disabled, ILSpy must emit explicit property
        // declarations (with an accessor block) for the record's components.
        // The positional form `record struct BugTwoRecordFixture(int Bar, ...)`
        // would lack these accessor blocks and fail this regex match.
        Assert.Matches(new Regex(@"\bint\s+Bar\s*\{[^}]*get"), output);
        Assert.Matches(new Regex(@"\bstring\s+Baz\s*\{[^}]*get"), output);
    }

    // Language-version target: setting the language version to C# 7.3 should
    // strip features that landed in later versions. Records arrived in C# 9
    // so a 7.3 target must NOT emit the `record` keyword anywhere in the
    // output — ILSpy back-rewrites the type as a regular struct.
    [Fact]
    public void DecompileType_record_struct_with_csharp_7_3_target_drops_record_keyword()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        settings.SetLanguageVersion(LanguageVersion.CSharp7_3);
        string output = decompiler.DecompileType(TestAssemblyPath, "RiderIlSpy.Tests.BugTwoRecordFixture", settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        Assert.False(string.IsNullOrEmpty(output));
        // `\brecord\s+(class|struct)\b` would match positional or non-positional
        // record syntax; both must be absent at the 7.3 target.
        Assert.DoesNotMatch(new Regex(@"\brecord\s+(class|struct)\b"), output);
    }

    // Paired with the test above: confirms Latest target DOES emit the
    // `record` keyword for the same fixture — proves the assertion above is
    // actually discriminating between language versions, not always passing.
    [Fact]
    public void DecompileType_record_struct_with_latest_target_keeps_record_keyword()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings();
        // Latest is the default — being explicit here documents intent.
        settings.SetLanguageVersion(LanguageVersion.Latest);
        string output = decompiler.DecompileType(TestAssemblyPath, "RiderIlSpy.Tests.BugTwoRecordFixture", settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        Assert.False(string.IsNullOrEmpty(output));
        Assert.Matches(new Regex(@"\brecord\s+struct\b"), output);
    }

    // Paired with the test above: confirms the same regex would NOT match when
    // primary-ctor syntax IS enabled — i.e. the regression test above is
    // actually discriminating between the two branches, not always passing.
    [Fact]
    public void DecompileType_record_struct_with_primary_ctor_enabled_emits_positional_form()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        DecompilerSettings settings = new DecompilerSettings
        {
            UsePrimaryConstructorSyntax = true,
        };
        string output = decompiler.DecompileType(TestAssemblyPath, "RiderIlSpy.Tests.BugTwoRecordFixture", settings, extraSearchDirs: null, mode: IlSpyOutputMode.CSharp);
        Assert.False(string.IsNullOrEmpty(output));
        // Positional record-struct syntax: `record struct BugTwoRecordFixture(int Bar, string Baz)`.
        // The components must appear inside the type's parameter list, NOT as
        // standalone bodied property declarations.
        Assert.Matches(new Regex(@"record\s+struct\s+BugTwoRecordFixture\s*\("), output);
        Assert.DoesNotMatch(new Regex(@"\bint\s+Bar\s*\{[^}]*get"), output);
    }

    // Feature #3: whole-assembly project export. The helper wraps ILSpy's
    // WholeProjectDecompiler so the user can save a decompiled assembly as a
    // buildable .csproj tree from the IDE. Smoke-test by exporting the test
    // assembly itself to a temp dir and asserting that:
    //   1. A .csproj is written to the output root.
    //   2. At least one .cs file lands somewhere under the tree.
    //   3. The result record's counts match the on-disk reality.
    // Anything more specific than this drifts with every ILSpy release because
    // the project layout (Properties/, obj/, namespace folders) is an
    // implementation detail of WholeProjectDecompiler, not part of its contract.
    [Fact]
    public void DecompileAssemblyToProject_writes_csproj_and_source_files()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string tempRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpy_ProjectExport_" + Path.GetRandomFileName());
        try
        {
            DecompilerSettings settings = new DecompilerSettings();
            DecompileAssemblyToProjectResult result = decompiler.DecompileAssemblyToProject(TestAssemblyPath, tempRoot, settings);

            Assert.Equal(tempRoot, result.OutputDirectory);
            Assert.NotNull(result.ProjectFilePath);
            Assert.True(File.Exists(result.ProjectFilePath!), $"expected .csproj on disk at {result.ProjectFilePath}");
            Assert.EndsWith(".csproj", result.ProjectFilePath!);
            Assert.True(result.CSharpFileCount > 0, "expected at least one .cs file written");

            int actualCs = Directory.EnumerateFiles(tempRoot, "*.cs", SearchOption.AllDirectories).Count();
            Assert.Equal(actualCs, result.CSharpFileCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // Paired with the test above: confirms the helper creates the target
    // directory if it does not already exist (rather than throwing). This is
    // the realistic UX path — the user picks a brand-new folder in a dir
    // picker, the folder may not exist yet, and the helper should just write.
    [Fact]
    public void DecompileAssemblyToProject_creates_target_directory_if_missing()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string tempRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpy_ProjectExport_Missing_" + Path.GetRandomFileName());
        Assert.False(Directory.Exists(tempRoot), "precondition: temp dir should not exist yet");
        try
        {
            DecompilerSettings settings = new DecompilerSettings();
            DecompileAssemblyToProjectResult result = decompiler.DecompileAssemblyToProject(TestAssemblyPath, tempRoot, settings);
            Assert.True(Directory.Exists(tempRoot), "expected helper to create the target directory");
            Assert.True(result.CSharpFileCount > 0);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // Paired with the test above: confirms settings flow through to the
    // project output the same way they flow through per-type decompile. With
    // primary-ctor syntax disabled, the BugTwoRecordFixture should appear as
    // bodied properties somewhere in the exported project, not as positional
    // form. This pins the contract that BuildDecompilerSettings (from the
    // production code path) is the right knob for project export too.
    [Fact]
    public void DecompileAssemblyToProject_respects_decompiler_settings()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string tempRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpy_ProjectExport_Settings_" + Path.GetRandomFileName());
        try
        {
            DecompilerSettings settings = new DecompilerSettings
            {
                UsePrimaryConstructorSyntax = false,
            };
            decompiler.DecompileAssemblyToProject(TestAssemblyPath, tempRoot, settings);
            bool anyMatch = Directory.EnumerateFiles(tempRoot, "*.cs", SearchOption.AllDirectories)
                .Any(f => Regex.IsMatch(File.ReadAllText(f), @"\bint\s+Bar\s*\{[^}]*get"));
            Assert.True(anyMatch, "expected at least one .cs file to contain a bodied property for BugTwoRecordFixture.Bar");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
