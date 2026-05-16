using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace RiderIlSpy;

internal sealed class MixedMethodBodyDisassembler : MethodBodyDisassembler
{
    private readonly DecompilerSettings mySettings;
    private readonly IAssemblyResolver myResolver;
    private List<SequencePoint>? mySequencePoints;
    private string[]? myCodeLines;

    public MixedMethodBodyDisassembler(ITextOutput output, CancellationToken cancellationToken, DecompilerSettings settings, IAssemblyResolver resolver)
        : base(output, cancellationToken)
    {
        mySettings = settings;
        myResolver = resolver;
    }

    public override void Disassemble(PEFile module, MethodDefinitionHandle handle)
    {
        try
        {
            CSharpDecompiler decompiler = new CSharpDecompiler(module, myResolver, mySettings);
            SyntaxTree syntaxTree = decompiler.Decompile(handle);

            using StringWriter csOutput = new StringWriter();
            WriteCode(csOutput, mySettings, syntaxTree);

            Dictionary<ICSharpCode.Decompiler.IL.ILFunction, List<SequencePoint>> all = decompiler.CreateSequencePoints(syntaxTree);
            KeyValuePair<ICSharpCode.Decompiler.IL.ILFunction, List<SequencePoint>> mapping = all.FirstOrDefault(kvp =>
            {
                ICSharpCode.Decompiler.TypeSystem.IMethod? m = kvp.Key.MoveNextMethod ?? kvp.Key.Method;
                if (m == null) return false;
                return m.MetadataToken == (EntityHandle)handle;
            });

            mySequencePoints = mapping.Value ?? new List<SequencePoint>();
            myCodeLines = csOutput.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            base.Disassemble(module, handle);
        }
        finally
        {
            mySequencePoints = null;
            myCodeLines = null;
        }
    }

    protected override void WriteInstruction(ITextOutput output, MetadataReader metadata, MethodDefinitionHandle methodHandle, ref BlobReader blob, int methodRva)
    {
        if (mySequencePoints != null && myCodeLines != null)
        {
            int index = BinarySearchByOffset(mySequencePoints, blob.Offset);
            if (index >= 0)
            {
                SequencePoint info = mySequencePoints[index];
                if (!info.IsHidden)
                {
                    for (int line = info.StartLine; line <= info.EndLine; line++)
                    {
                        if (line < 1 || line > myCodeLines.Length) continue;
                        string text = myCodeLines[line - 1];
                        output.WriteLine("// " + text);
                    }
                }
                else
                {
                    output.WriteLine("// (no C# code)");
                }
            }
        }
        base.WriteInstruction(output, metadata, methodHandle, ref blob, methodRva);
    }

    private static void WriteCode(TextWriter output, DecompilerSettings settings, SyntaxTree syntaxTree)
    {
        syntaxTree.AcceptVisitor(new InsertParenthesesVisitor { InsertParenthesesForReadability = true });
        TokenWriter tokenWriter = new TextWriterTokenWriter(output) { IndentationString = settings.CSharpFormattingOptions.IndentationString };
        tokenWriter = TokenWriter.WrapInWriterThatSetsLocationsInAST(tokenWriter);
        syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
    }

    private static int BinarySearchByOffset(List<SequencePoint> list, int offset)
    {
        int lo = 0;
        int hi = list.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int midOffset = list[mid].Offset;
            if (midOffset == offset) return mid;
            if (midOffset < offset) lo = mid + 1;
            else hi = mid - 1;
        }
        return ~lo;
    }
}
