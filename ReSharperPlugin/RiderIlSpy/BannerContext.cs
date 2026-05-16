using System.Collections.Generic;

namespace RiderIlSpy;

/// <summary>
/// Bundles the per-decompile data that the diagnostic banner needs. Replaces a
/// recurring 5-positional-arg shape that flowed through every banner helper
/// (and read like a tuple of unrelated values at the call site). With this
/// record the banner helpers reduce to <c>(BannerContext, content)</c> and
/// <c>(BannerContext, SourceLinkOutcome?, content)</c>.
/// </summary>
/// <param name="Meta">Identity metadata read from the assembly's CLI header.
/// <c>null</c> is tolerated — the banner falls back to a path/mode summary so
/// it stays useful for assembly-resolution diagnostics.</param>
/// <param name="AssemblyPath">Absolute path to the decompiled assembly on disk.
/// Surfaced in the banner (redacted via $HOME) so users can spot the resolved
/// reference at a glance.</param>
/// <param name="TypeFullName">CLR-style full name of the decompiled type
/// (<c>Namespace.Outer+Inner</c>); appears on the banner's Type line.</param>
/// <param name="Mode">Output mode (CSharp / IL / CSharpWithIL); appears on the
/// banner's Mode line and drives SourceLink eligibility upstream.</param>
/// <param name="ExtraSearchDirs">Extra assembly search dirs passed to the
/// resolver. Each entry is redacted in the banner; empty list renders as
/// <c>(none)</c>.</param>
public sealed record BannerContext(
    AssemblyBannerMetadata? Meta,
    string AssemblyPath,
    string TypeFullName,
    IlSpyOutputMode Mode,
    IReadOnlyList<string> ExtraSearchDirs);
