using System;
using System.Text;

namespace RiderIlSpy;

/// <summary>
/// Wraps an exception thrown out of ICSharpCode.Decompiler into a comment-only
/// C# file that Rider can display. Includes the full exception chain + stack
/// trace so the user can copy/paste it into a bug report without us needing
/// a second round-trip. Pulled out of <see cref="IlSpyDecompiler"/> so the
/// formatting contract (comment shape, indent depth, divider placement) can
/// be unit-tested without standing up the full decompiler.
/// </summary>
internal static class DecompileFailureFormatter
{
    public static string Format(string typeFullName, Exception ex)
    {
        StringBuilder sb = new StringBuilder();
        CommentBlock.Line(sb, "RiderIlSpy decompile failed for " + typeFullName);
        CommentBlock.Divider(sb);
        CommentBlock.Line(sb, "This is almost always an ICSharpCode.Decompiler bug. Please file an issue at");
        CommentBlock.Line(sb, "https://github.com/cryptiklemur/rider-ilspy/issues with the type name and the");
        CommentBlock.Line(sb, "trace below.");
        CommentBlock.Divider(sb);
        Exception? current = ex;
        int depth = 0;
        while (current != null)
        {
            CommentBlock.IndentedLine(sb, depth, current.GetType().FullName + ": " + current.Message);
            if (!string.IsNullOrEmpty(current.StackTrace))
                foreach (string line in current.StackTrace.Split('\n'))
                    CommentBlock.IndentedLine(sb, depth + 1, line.TrimEnd('\r'));
            current = current.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
