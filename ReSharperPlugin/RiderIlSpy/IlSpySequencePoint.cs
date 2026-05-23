namespace RiderIlSpy;

/// <summary>
/// One sequence point produced by ICSharpCode.Decompiler's CreateSequencePoints —
/// the (ilOffset, source-region) pair the debugger needs to bind a breakpoint
/// on a decompiled source line back to an IL offset in the real method body.
/// Kept as a plain record (no ICSharpCode.Decompiler.DebugInfo.SequencePoint
/// reference) so downstream pure helpers (banner offset translation, hidden-point
/// passthrough, JetBrains DebugData construction) can be unit-tested without
/// dragging in the ILSpy or JetBrains SDK type graphs.
/// </summary>
/// <param name="IlOffset">Offset (in IL bytes) within the method body that this point covers.</param>
/// <param name="StartLine">1-based source line for the start of the point's region.</param>
/// <param name="StartColumn">1-based source column for the start of the point's region.</param>
/// <param name="EndLine">1-based source line for the end of the region (inclusive).</param>
/// <param name="EndColumn">1-based source column for the end of the region (inclusive).</param>
/// <param name="IsHidden">True for the portable-PDB "hidden" sentinel point (compiler-generated code with no user source).</param>
public sealed record IlSpySequencePoint(
    int IlOffset,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    bool IsHidden);
