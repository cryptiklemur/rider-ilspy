using ICSharpCode.Decompiler.DebugInfo;
using Xunit;

namespace RiderIlSpy.Tests;

// CrosslinkMarker carries the IL↔C# mapping line that ships in mixed-mode
// disassembly output. The format is the wire contract between this backend
// and the Kotlin frontend's IL navigation index — these tests pin both
// directions so a careless edit to Format would fail TryParse in the same
// CI run, instead of silently breaking IDE navigation for end users.
public class CrosslinkMarkerTests
{
    [Fact]
    public void Format_emits_canonical_line_for_normal_sequence_point()
    {
        SequencePoint sp = new SequencePoint
        {
            StartLine = 42,
            EndLine = 44,
            StartColumn = 5,
            EndColumn = 21,
        };
        string? marker = CrosslinkMarker.Format(0x000F, sp);
        Assert.Equal("// ⟪@cs il=0x000F line=42-44 col=5-21⟫", marker);
    }

    [Fact]
    public void Format_pads_offset_to_four_hex_digits()
    {
        SequencePoint sp = new SequencePoint { StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 2 };
        string? marker = CrosslinkMarker.Format(0x7, sp);
        Assert.NotNull(marker);
        Assert.Contains("il=0x0007 ", marker);
    }

    [Fact]
    public void Format_returns_null_for_hidden_sequence_point()
    {
        // Hidden sequence points have StartLine == 0xfeefee per ECMA-335 — the
        // mixed disassembler already prints "// (no C# code)" for those, so
        // the crosslink marker is omitted to avoid emitting a meaningless
        // line=0xfeefee that confuses parsers downstream.
        SequencePoint sp = new SequencePoint
        {
            StartLine = 0xfeefee,
            EndLine = 0xfeefee,
            StartColumn = 0,
            EndColumn = 0,
        };
        string? marker = CrosslinkMarker.Format(0, sp);
        Assert.Null(marker);
    }

    [Fact]
    public void TryParse_roundtrips_a_formatted_marker()
    {
        SequencePoint sp = new SequencePoint { StartLine = 100, EndLine = 102, StartColumn = 8, EndColumn = 19 };
        string marker = CrosslinkMarker.Format(0x1234, sp)!;
        Assert.True(CrosslinkMarker.TryParse(marker, out CrosslinkInfo info));
        Assert.Equal(0x1234, info.IlOffset);
        Assert.Equal(100, info.StartLine);
        Assert.Equal(102, info.EndLine);
        Assert.Equal(8, info.StartColumn);
        Assert.Equal(19, info.EndColumn);
    }

    [Fact]
    public void TryParse_tolerates_leading_whitespace()
    {
        // ReflectionDisassembler indents method bodies; the marker appears at
        // column 0 of the disassembly output but a parser walking from a
        // pre-trimmed buffer might see indentation. Tolerate it.
        string padded = "    // ⟪@cs il=0x0001 line=1-1 col=0-0⟫";
        Assert.True(CrosslinkMarker.TryParse(padded, out CrosslinkInfo info));
        Assert.Equal(1, info.IlOffset);
    }

    [Fact]
    public void TryParse_returns_false_for_regular_comment()
    {
        Assert.False(CrosslinkMarker.TryParse("// just a normal comment", out _));
    }

    [Fact]
    public void TryParse_returns_false_for_empty_or_null()
    {
        Assert.False(CrosslinkMarker.TryParse("", out _));
        Assert.False(CrosslinkMarker.TryParse(null!, out _));
    }

    [Fact]
    public void TryParse_returns_false_when_suffix_missing()
    {
        // Truncated line — easy to produce if the disassembler output is
        // line-buffered and a write got cut. The parser must not crash on it.
        string truncated = "// ⟪@cs il=0x0001 line=1-1 col=0-0";
        Assert.False(CrosslinkMarker.TryParse(truncated, out _));
    }

    [Fact]
    public void TryParse_returns_false_when_il_offset_missing()
    {
        string broken = "// ⟪@cs line=1-1 col=0-0⟫";
        Assert.False(CrosslinkMarker.TryParse(broken, out _));
    }

    [Fact]
    public void TryParse_returns_false_when_line_range_malformed()
    {
        // No dash in the range — not enough information to recover. Better to
        // fail closed than to guess.
        string broken = "// ⟪@cs il=0x0001 line=42 col=0-0⟫";
        Assert.False(CrosslinkMarker.TryParse(broken, out _));
    }

    [Fact]
    public void TryParse_accepts_marker_without_col_range()
    {
        // The col= field is optional — older emitters or simplified outputs
        // may omit it. Line range is the load-bearing piece for navigation.
        string lineOnly = "// ⟪@cs il=0x0042 line=7-9⟫";
        Assert.True(CrosslinkMarker.TryParse(lineOnly, out CrosslinkInfo info));
        Assert.Equal(0x42, info.IlOffset);
        Assert.Equal(7, info.StartLine);
        Assert.Equal(9, info.EndLine);
        Assert.Equal(0, info.StartColumn);
        Assert.Equal(0, info.EndColumn);
    }

    [Fact]
    public void Format_and_TryParse_handle_maximum_il_offset()
    {
        // 0xFFFF is the largest single-method IL offset (CIL methods cap at 64KB).
        SequencePoint sp = new SequencePoint { StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 2 };
        string marker = CrosslinkMarker.Format(0xFFFF, sp)!;
        Assert.True(CrosslinkMarker.TryParse(marker, out CrosslinkInfo info));
        Assert.Equal(0xFFFF, info.IlOffset);
    }
}
