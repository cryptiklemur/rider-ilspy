using System.Collections.Generic;
using JetBrains.Metadata.Access;
using JetBrains.Metadata.Debug;
using JetBrains.Metadata.Reader.API;

namespace RiderIlSpy;

/// <summary>
/// Bridges the SDK-free <see cref="MethodSequencePoints"/> records produced by
/// <see cref="IlSpyDecompiler"/> into JetBrains' debug-data graph
/// (<see cref="DebugData"/>, <see cref="DebugDocument"/>, <see cref="DebugMethod"/>,
/// <see cref="SequencePoint"/>) the platform debugger needs to bind breakpoints
/// in decompiled source. Sits at the SDK boundary on purpose — the upstream
/// pipeline stays free of <c>JetBrains.Metadata.Debug</c> types so its pure
/// helpers stay unit-testable.
/// </summary>
/// <remarks>
/// Not directly unit-tested. The test project does not link
/// <c>JetBrains.Rider.Backend</c> (PrivateAssets in the production csproj),
/// so any test that constructed a <see cref="DebugData"/> would force the test
/// harness to pull the whole Rider SDK. The pure offset/line-count logic this
/// class consumes lives in <c>IlSpyExternalSourcesProviderHelpers</c> and is
/// exhaustively covered there; the SDK construction surface is validated by
/// the runIde debugger smoke test (set a breakpoint in decompiled BCL source,
/// confirm it binds and hits).
/// </remarks>
internal static class DebugDataFactory
{
    /// <summary>
    /// Construct a <see cref="DebugData"/> graph for one decompiled source file.
    /// All methods are registered against a single synthetic
    /// <see cref="DebugDocument"/> whose URL is <paramref name="sourceUrl"/> —
    /// the cache stores one file per (assembly, type, mode) triple so a single
    /// document is the correct grain. Sequence-point line numbers are shifted by
    /// <paramref name="bannerLineCount"/> so they line up with the
    /// banner-prefixed text the debugger reads from the cache.
    /// </summary>
    /// <remarks>
    /// <para>Returns <c>null</c> when there is nothing useful to register —
    /// either the decompiler emitted no methods, or every method had zero
    /// sequence points (body-less / abstract / extern). Caller treats null as
    /// "no breakpoint binding available" and skips storing debug data for the
    /// entry, identical to the legacy behavior.</para>
    /// <para><c>importScope</c> and <c>methodCustomDebugInformation</c> are
    /// always null. The SDK marks both <c>[CanBeNull]</c> in CreateMethod's
    /// signature — passing null is the documented "no PDB import scope / no
    /// extra debug info" path, and ILSpy does not surface either from its
    /// sequence-point pass. If a future iteration needs hoisted-locals scopes or
    /// async-state-machine maps, those flow through here.</para>
    /// </remarks>
    public static DebugData? BuildDebugData(
        string sourceUrl,
        IReadOnlyList<MethodSequencePoints> methods,
        int bannerLineCount)
    {
        if (methods.Count == 0) return null;

        IReadOnlyList<MethodSequencePoints> shiftedMethods =
            IlSpyExternalSourcesProviderHelpers.OffsetSequencePointsByBannerLines(methods, bannerLineCount);

        DebugData data = DebugData.CreateEmptyDebugData();
        DebugDocument document = data.CreateDocument(sourceUrl);
        int documentIndex = document.Index;
        bool createdAnyMethod = false;

        foreach (MethodSequencePoints method in shiftedMethods)
        {
            if (method.Points.Count == 0) continue;

            List<SequencePoint> sequencePoints = new List<SequencePoint>(method.Points.Count);
            foreach (IlSpySequencePoint point in method.Points)
            {
                sequencePoints.Add(point.IsHidden
                    ? SequencePoint.CreateHiddenSequencePoint(documentIndex, point.IlOffset)
                    : new SequencePoint(documentIndex, point.IlOffset, point.StartLine, point.StartColumn, point.EndLine, point.EndColumn));
            }

            MetadataToken methodToken = new MetadataToken((uint)method.MethodToken);
            MetadataToken localSignatureToken = new MetadataToken((uint)method.LocalSignatureToken);
            data.CreateMethod(
                methodToken,
                localSignatureToken,
                method.CodeLength,
                importScope: null,
                sequencePoints: sequencePoints,
                methodCustomDebugInformation: null);
            createdAnyMethod = true;
        }

        return createdAnyMethod ? data : null;
    }

    /// <summary>
    /// Wraps a <see cref="DebugData"/> as the <see cref="ExtendedDebugData"/>
    /// shape the cache stores and the debugger ingests. <see cref="DebugDataOrigin.Decompiled"/>
    /// tells the platform this debug info is reconstructed from decompiled
    /// source rather than a real PDB — Rider uses that to flag breakpoints
    /// appropriately in the UI.
    /// </summary>
    public static ExtendedDebugData WrapForAssembly(
        DebugData data,
        AssemblyId assemblyId,
        string assemblyPath,
        string assemblyFullName)
        => ExtendedDebugData.Create(data, assemblyId, assemblyPath, assemblyFullName, DebugDataOrigin.Decompiled);
}
