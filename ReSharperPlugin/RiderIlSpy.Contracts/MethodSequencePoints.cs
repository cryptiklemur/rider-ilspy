using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// All debug-info for one decompiled method: the metadata token + body size
/// (needed by JetBrains <c>DebugData.CreateMethod</c>'s codeLength contract), the
/// local-variable signature token (0 when the method has no locals), and the
/// per-method sequence points the debugger uses to map source lines to IL offsets.
/// Built per-type during decompile by <see cref="IlSpyDecompiler"/> and threaded
/// through <see cref="DecompileResult"/>.<see cref="DecompileResult.Methods"/> so the
/// provider layer can later translate them into JetBrains <c>DebugData</c> for
/// the cache + the <c>GetSourceDebugData</c>/<c>GetTypeDebugData</c> contract.
/// </summary>
/// <param name="MethodToken">Full metadata token (table+rid packed int) of the method this entry describes — sourced from <c>ILFunction.MoveNextMethod ?? ILFunction.Method</c> so iterator/async state-machine MoveNext methods get the SPs they actually need.</param>
/// <param name="LocalSignatureToken">Metadata token of the StandaloneSignature row for local variables, or 0 when the method has no body / no locals.</param>
/// <param name="CodeLength">Length of the method's IL byte stream; 0 when the method has no body (abstract / extern).</param>
/// <param name="Points">Sequence points emitted by ICSharpCode.Decompiler for this method, in IL order. Empty when the decompiler produced no debug info (e.g. body-less methods).</param>
public sealed record MethodSequencePoints(
    int MethodToken,
    int LocalSignatureToken,
    int CodeLength,
    IReadOnlyList<IlSpySequencePoint> Points);
