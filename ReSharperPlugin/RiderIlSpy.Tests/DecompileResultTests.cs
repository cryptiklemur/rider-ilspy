using System.IO;
using RiderIlSpy;
using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Pins the <see cref="DecompileResult"/> contract: callers branch on
/// <see cref="DecompileResult.Success"/> to distinguish a real decompile from
/// a comment-prefixed failure trace, and read <see cref="DecompileResult.FailureReason"/>
/// for log entries without parsing the full <see cref="DecompileResult.Content"/>.
/// </summary>
public sealed class DecompileResultTests
{
    [Fact]
    public void Ok_factory_marks_success_and_clears_failure_reason()
    {
        DecompileResult r = DecompileResult.Ok("namespace X { class Y {} }");

        Assert.True(r.Success);
        Assert.Null(r.FailureReason);
        Assert.Equal("namespace X { class Y {} }", r.Content);
    }

    [Fact]
    public void Fail_factory_marks_failure_and_carries_reason()
    {
        DecompileResult r = DecompileResult.Fail("// failed: see trace below\n", "FileNotFoundException: missing.dll");

        Assert.False(r.Success);
        Assert.Equal("FileNotFoundException: missing.dll", r.FailureReason);
        Assert.Equal("// failed: see trace below\n", r.Content);
    }

    [Fact]
    public void DecompileType_for_missing_assembly_path_returns_failure()
    {
        // Reaching the catch path means the runtime contract is honored:
        // top-level exceptions surface as DecompileResult.Fail with a non-null
        // FailureReason rather than a bare comment-prefixed string. The test
        // assembly path is bogus so PEFile bails inside the try.
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpy-DecompileResultTests-missing.dll");

        DecompileResult r = decompiler.DecompileType(
            fake,
            "Some.Type.That.WontBeRead",
            new ICSharpCode.Decompiler.DecompilerSettings(),
            extraSearchDirs: null,
            mode: IlSpyOutputMode.CSharp);

        Assert.False(r.Success);
        Assert.NotNull(r.FailureReason);
        Assert.Contains("RiderIlSpy decompile failed", r.Content);
    }

    [Fact]
    public void DecompileType_for_real_assembly_returns_success()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string asmPath = typeof(DecompileResultTests).Assembly.Location;
        Assert.True(File.Exists(asmPath));

        DecompileResult r = decompiler.DecompileType(
            asmPath,
            "RiderIlSpy.Tests.DecompileResultTests",
            new ICSharpCode.Decompiler.DecompilerSettings(),
            extraSearchDirs: null,
            mode: IlSpyOutputMode.CSharp);

        Assert.True(r.Success);
        Assert.Null(r.FailureReason);
        Assert.False(string.IsNullOrEmpty(r.Content));
    }

    [Fact]
    public void DecompileAssemblyInfo_for_missing_path_returns_failure()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpy-DecompileResultTests-missing-info.dll");

        DecompileResult r = decompiler.DecompileAssemblyInfo(fake);

        Assert.False(r.Success);
        Assert.NotNull(r.FailureReason);
    }

    [Fact]
    public void DecompileAssemblyInfo_for_real_assembly_returns_success()
    {
        IlSpyDecompiler decompiler = new IlSpyDecompiler();
        string asmPath = typeof(DecompileResultTests).Assembly.Location;

        DecompileResult r = decompiler.DecompileAssemblyInfo(asmPath);

        Assert.True(r.Success);
        Assert.Null(r.FailureReason);
        Assert.False(string.IsNullOrEmpty(r.Content));
    }
}
