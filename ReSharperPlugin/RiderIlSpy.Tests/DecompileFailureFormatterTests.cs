using System;
using Xunit;

namespace RiderIlSpy.Tests;

// Pins the rendered shape of decompile-failure comment blocks. These end up
// in the user's Rider tab as the "source" of a failed decompile, so layout
// regressions (missing dividers, drifted indent, dropped inner-exception
// chain) silently make the failure unreadable. Tests are pure-helper-level
// so they don't need IlSpyDecompiler / ICSharpCode.Decompiler to run.
public class DecompileFailureFormatterTests
{
    [Fact]
    public void Format_includes_type_name_in_header()
    {
        Exception ex = new InvalidOperationException("boom");
        string output = DecompileFailureFormatter.Format("Some.TypeName", ex);
        Assert.Contains("decompile failed for Some.TypeName", output);
    }

    [Fact]
    public void Format_includes_issue_tracker_link()
    {
        string output = DecompileFailureFormatter.Format("X", new Exception("boom"));
        Assert.Contains("github.com/cryptiklemur/rider-ilspy/issues", output);
    }

    [Fact]
    public void Format_renders_full_inner_exception_chain()
    {
        Exception inner = new ArgumentException("argument bad");
        Exception outer = new InvalidOperationException("operation bad", inner);
        string output = DecompileFailureFormatter.Format("X", outer);
        Assert.Contains("InvalidOperationException: operation bad", output);
        Assert.Contains("ArgumentException: argument bad", output);
    }

    [Fact]
    public void Format_indents_inner_exceptions_deeper()
    {
        Exception inner = new ArgumentException("inner-msg");
        Exception outer = new InvalidOperationException("outer-msg", inner);
        string output = DecompileFailureFormatter.Format("X", outer);

        // Outer exception (depth 0) line should start with "// outer-msg-type"
        // — no extra leading space after the "// " prefix.
        Assert.Contains("// System.InvalidOperationException: outer-msg", output);
        // Inner exception (depth 1) line should have one indent step (2 spaces)
        // after the "// " prefix — pins the depth-vs-indent contract.
        Assert.Contains("//   System.ArgumentException: inner-msg", output);
    }

    [Fact]
    public void Format_emits_comment_only_output()
    {
        string output = DecompileFailureFormatter.Format("X", new Exception("boom"));
        foreach (string line in output.Split('\n'))
        {
            if (line.Length == 0) continue;
            Assert.StartsWith("//", line);
        }
    }
}
