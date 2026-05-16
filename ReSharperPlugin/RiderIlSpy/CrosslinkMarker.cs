using System;
using System.Globalization;
using ICSharpCode.Decompiler.DebugInfo;

namespace RiderIlSpy;

/// <summary>
/// Emits and parses the machine-readable comment line that maps an IL offset
/// to its C# source location in mixed-mode disassembly output.
///
/// Format: <c>// ⟪@cs il=0xHHHH line=START-END col=START-END⟫</c>
///
/// <para>Why a custom line marker rather than reusing standard PDB sequence
/// points or LLVM-style <c>!dbg</c> metadata?</para>
/// <list type="bullet">
///   <item>The output of <see cref="MixedMethodBodyDisassembler"/> is plain
///         text consumed by the Kotlin frontend's text editor — there's no
///         structured channel to ship a sidecar manifest.</item>
///   <item>The marker has to round-trip through the line-based viewer the
///         user actually sees, so it lives inline in the disassembly as a
///         comment that humans can also read.</item>
///   <item>The triangular brackets (U+27EA / U+27EB) keep the marker
///         lexically distinct from human-authored <c>// </c> comments and
///         from ILSpy's existing <c>// IL_xxxx</c> labels — easy to grep,
///         hard to collide with.</item>
/// </list>
///
/// <para>The format is intentionally line-oriented so the Kotlin side can
/// parse it with a single regex when building its IL↔C# crosslink index.</para>
/// </summary>
public static class CrosslinkMarker
{
    /// <summary>Sentinel prefix the parser uses to recognize a crosslink line.</summary>
    public const string Prefix = "// ⟪@cs ";

    /// <summary>Sentinel suffix that closes a crosslink line.</summary>
    public const string Suffix = "⟫";

    /// <summary>
    /// Builds the marker line for <paramref name="ilOffset"/> mapped to the
    /// given sequence-point span. Returns <c>null</c> when the sequence point
    /// is hidden (the C# source doesn't correspond to any user code at this
    /// IL offset, e.g. compiler-generated state-machine plumbing).
    /// </summary>
    public static string? Format(int ilOffset, SequencePoint sp)
    {
        if (sp.IsHidden) return null;
        // The hex offset uses 4 digits — IL method bodies cap at 64KB so 4
        // is always enough and keeps the marker fixed-width-ish for humans.
        string offsetHex = ilOffset.ToString("X4", CultureInfo.InvariantCulture);
        return string.Concat(
            Prefix,
            "il=0x", offsetHex,
            " line=", sp.StartLine.ToString(CultureInfo.InvariantCulture),
            "-", sp.EndLine.ToString(CultureInfo.InvariantCulture),
            " col=", sp.StartColumn.ToString(CultureInfo.InvariantCulture),
            "-", sp.EndColumn.ToString(CultureInfo.InvariantCulture),
            Suffix);
    }

    /// <summary>
    /// Parses a marker line back into its components. Returns <c>false</c>
    /// when the line is not a marker (or is malformed), without throwing.
    /// </summary>
    public static bool TryParse(string line, out CrosslinkInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(line)) return false;
        // Trim leading whitespace — disassembly is indented after the IL_xxxx
        // labels, and the marker is emitted at column 0, but we want the
        // parser tolerant to either case.
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        if (!trimmed.EndsWith(Suffix, StringComparison.Ordinal)) return false;

        string body = trimmed.Substring(Prefix.Length, trimmed.Length - Prefix.Length - Suffix.Length);
        string[] parts = body.Split(' ');
        int? offset = null;
        int? startLine = null;
        int? endLine = null;
        int? startCol = null;
        int? endCol = null;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p.StartsWith("il=0x", StringComparison.Ordinal))
            {
                if (int.TryParse(p.Substring(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                    offset = v;
            }
            else if (p.StartsWith("line=", StringComparison.Ordinal))
            {
                ParseRange(p.Substring(5), out startLine, out endLine);
            }
            else if (p.StartsWith("col=", StringComparison.Ordinal))
            {
                ParseRange(p.Substring(4), out startCol, out endCol);
            }
        }
        if (offset == null || startLine == null || endLine == null) return false;
        info = new CrosslinkInfo(offset.Value, startLine.Value, endLine.Value, startCol ?? 0, endCol ?? 0);
        return true;
    }

    private static void ParseRange(string range, out int? lo, out int? hi)
    {
        lo = null;
        hi = null;
        int dash = range.IndexOf('-');
        if (dash < 0) return;
        if (!int.TryParse(range.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int l)) return;
        if (!int.TryParse(range.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return;
        lo = l;
        hi = h;
    }
}

/// <summary>Parsed crosslink marker — the inverse of <see cref="CrosslinkMarker.Format"/>.</summary>
public readonly struct CrosslinkInfo
{
    public CrosslinkInfo(int ilOffset, int startLine, int endLine, int startColumn, int endColumn)
    {
        IlOffset = ilOffset;
        StartLine = startLine;
        EndLine = endLine;
        StartColumn = startColumn;
        EndColumn = endColumn;
    }

    public int IlOffset { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public int StartColumn { get; }
    public int EndColumn { get; }
}
