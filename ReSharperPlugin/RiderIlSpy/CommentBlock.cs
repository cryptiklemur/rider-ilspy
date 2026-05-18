using System.Text;

namespace RiderIlSpy;

/// <summary>
/// Tiny shared comment-formatting helper. Decompile-failure paths build C#
/// comment blocks that Rider renders as decompiled "source"; centralizing
/// the <c>// </c> prefix and divider conventions keeps the rendered layout
/// consistent and removes the hand-rolled StringBuilder dance from each
/// call site. Lives at the top level (rather than nested inside
/// <see cref="IlSpyDecompiler"/>) so both <see cref="DecompileFailureFormatter"/>
/// and the per-mode fallback paths can share it without going through the
/// decompiler class.
/// </summary>
internal static class CommentBlock
{
    public static StringBuilder Line(StringBuilder sb, string text) => sb.Append("// ").Append(text).Append('\n');

    public static StringBuilder Divider(StringBuilder sb) => sb.Append("//\n");

    public static StringBuilder IndentedLine(StringBuilder sb, int depth, string text)
    {
        sb.Append("// ");
        for (int i = 0; i < depth; i++) sb.Append("  ");
        return sb.Append(text).Append('\n');
    }
}
