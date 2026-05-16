using System;
using System.IO;
using Xunit;

namespace RiderIlSpy.Tests;

// These tests exercise the negative paths of PdbSourceLinkReader — the bits we
// CAN test without a checked-in SourceLink fixture. The positive paths (reading
// SourceLink JSON from a real assembly, walking its method debug info) need a
// known assembly with embedded PDB + SourceLink, which is host-specific (the
// .NET runtime ships such assemblies but their location varies by SDK version).
// They get exercised by the integration smoke we run against System.Private.CoreLib
// in Rider — the negative paths here ensure we never throw out of TryOpen so the
// fallback to ILSpy is always reachable.
public class PdbSourceLinkReaderTests
{
    [Fact]
    public void TryOpen_returns_null_for_nonexistent_path()
    {
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-nope-" + Guid.NewGuid().ToString("N") + ".dll");
        using PdbSourceLinkReader? reader = PdbSourceLinkReader.TryOpen(fake);
        Assert.Null(reader);
    }

    [Fact]
    public void TryOpen_returns_null_for_non_pe_file()
    {
        string fake = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-nonpe-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllText(fake, "not a PE file");
        try
        {
            using PdbSourceLinkReader? reader = PdbSourceLinkReader.TryOpen(fake);
            Assert.Null(reader);
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public void TryOpen_succeeds_on_real_managed_assembly_without_throwing()
    {
        // Smoke test: our own test assembly exists as a real managed PE with a
        // sidecar portable PDB. We can't assert SourceLink is absent because
        // some build/CI environments inject SourceLink via SDK defaults — but
        // we can assert the open + read calls return without throwing.
        string asmPath = typeof(PdbSourceLinkReaderTests).Assembly.Location;
        Assert.True(File.Exists(asmPath));

        using PdbSourceLinkReader? reader = PdbSourceLinkReader.TryOpen(asmPath);
        // Either the reader opened (PDB available) or null (PDB absent). Both
        // are acceptable — the contract is "no throws, optional reader".
        if (reader != null)
        {
            // Whatever the JSON is, the call must return string or null.
            _ = reader.TryReadSourceLinkJson();
        }
    }

    [Fact]
    public void TryGetPrimaryDocumentPath_returns_null_for_unknown_type()
    {
        string asmPath = typeof(PdbSourceLinkReaderTests).Assembly.Location;
        using PdbSourceLinkReader? reader = PdbSourceLinkReader.TryOpen(asmPath);
        if (reader == null) return; // no pdb available in this env — skip
        Assert.Null(reader.TryGetPrimaryDocumentPath("Some.Type.That.Definitely.Does.Not.Exist"));
    }

    // Regression test for the FileStream leak: PEReader was constructed with
    // PEStreamOptions.LeaveOpen but the underlying FileStream wasn't tracked or
    // disposed, so every successful TryOpen leaked an fd (and on Windows kept
    // the assembly file locked for write). Verify Dispose now releases the
    // file by copying a real PE to a temp location, opening + disposing the
    // reader, then asserting the temp file can be deleted (which would throw
    // IOException on Windows if a handle remained open).
    [Fact]
    public void Dispose_releases_underlying_filestream_so_file_can_be_deleted()
    {
        string srcPath = typeof(PdbSourceLinkReaderTests).Assembly.Location;
        string copyPath = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-disp-" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(srcPath, copyPath);
        try
        {
            PdbSourceLinkReader? reader = PdbSourceLinkReader.TryOpen(copyPath);
            // Even if no PDB sidecar exists alongside the copy, TryOpen may
            // still have succeeded via embedded portable PDB. Whichever path
            // ran, Dispose must release every owned handle.
            reader?.Dispose();
            // Delete is the cross-platform check: on Windows a leaked FileStream
            // would lock the file and File.Delete throws IOException. On Linux
            // it just exercises the disposal path without false-failing.
            File.Delete(copyPath);
            Assert.False(File.Exists(copyPath));
        }
        finally
        {
            if (File.Exists(copyPath))
                File.Delete(copyPath);
        }
    }
}
