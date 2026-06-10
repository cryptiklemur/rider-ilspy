using System.IO;
using System.Text.RegularExpressions;

namespace RiderIlSpy;

/// <summary>
/// Resolves XML documentation file candidates for a compiled assembly.
///
/// Strategy:
///   1. Sidecar: <c>&lt;dir&gt;/&lt;name&gt;.xml</c> — covers NuGet packages,
///      Mono profile assemblies, .NETFx Reference Assemblies, ILSpy's own
///      decompiler-emitted output, and anything else that follows the C#
///      compiler's default "xmldoc next to dll" convention.
///   2. .NET shared-runtime → ref-pack redirect: when the assembly lives under
///      <c>.../shared/Microsoft.&lt;X&gt;.App/&lt;ver&gt;/&lt;asm&gt;.dll</c>,
///      the xmldocs live in the parallel
///      <c>.../packs/Microsoft.&lt;X&gt;.App.Ref/&lt;ver&gt;/ref/&lt;tfm&gt;/</c>
///      tree. The redirect target is a directory because BCL impl assemblies
///      don't map 1:1 to ref-pack xmldoc files — type forwarders mean a type
///      decompiled from <c>System.Private.CoreLib.dll</c> has its xmldocs in
///      some OTHER ref-pack xml (e.g. <c>System.Numerics.Vectors.xml</c>), so
///      callers enumerate the whole ref directory.
///
/// All helpers are pure (string-only); callers do the I/O filtering with
/// <c>File.Exists</c> / <c>Directory.Exists</c> / <c>Directory.GetFiles</c>.
/// </summary>
public static class IlSpyXmlDocResolver
{
    // Captures the .NET shared-runtime layout used on both Linux and Windows:
    //   .../shared/Microsoft.<App>.App/<version>/<assembly>.dll
    // where <App> is one of "NETCore", "AspNetCore", "WindowsDesktop". The
    // version segment is path-quoted (no separator chars). Separator-agnostic:
    // matches forward slashes and backslashes so Windows paths work without
    // pre-normalization at the call site.
    private static readonly Regex SharedRuntimeImplPath = new Regex(
        @"^(?<root>.*)[/\\]shared[/\\]Microsoft\.(?<app>NETCore|AspNetCore|WindowsDesktop)\.App[/\\](?<ver>[^/\\]+)[/\\][^/\\]+\.dll$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the conventional sidecar XML doc path
    /// (<c>&lt;dir&gt;/&lt;name&gt;.xml</c>) or null when the input is empty
    /// or Path.ChangeExtension throws (malformed path). No I/O — caller
    /// filters with File.Exists.
    /// </summary>
    public static string? GetSidecarXmlDocPath(string? assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath)) return null;
        try { return Path.ChangeExtension(assemblyPath, ".xml"); }
        catch { return null; }
    }

    /// <summary>
    /// Maps a .NET shared-runtime impl assembly path to its parallel ref-pack
    /// xmldoc root directory, or null when the input doesn't live under a
    /// recognised <c>shared/Microsoft.&lt;X&gt;.App/&lt;ver&gt;</c> tree. The
    /// returned path always ends with <c>/ref</c> — caller enumerates the
    /// TFM subdirectories with <c>Directory.GetFiles(dir, "*.xml",
    /// SearchOption.AllDirectories)</c>. Separator style mirrors the input
    /// (forward when the input has no backslash, backslash otherwise) so the
    /// caller gets a host-canonical path.
    /// </summary>
    public static string? GetRefPackXmlDocDirectory(string? assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath)) return null;
        Match m = SharedRuntimeImplPath.Match(assemblyPath);
        if (!m.Success) return null;
        string root = m.Groups["root"].Value;
        string app = m.Groups["app"].Value;
        string ver = m.Groups["ver"].Value;
        char sep = assemblyPath.IndexOf('\\') >= 0 ? '\\' : '/';
        return string.Concat(root, sep, "packs", sep, "Microsoft.", app, ".App.Ref", sep, ver, sep, "ref");
    }
}
