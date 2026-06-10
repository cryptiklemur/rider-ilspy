using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RiderIlSpy;

// Pure helpers separated from IlSpyExternalSourcesProvider so unit tests can
// exercise them without dragging in JetBrains.ReSharper.Feature.Services (and
// the rest of the ReSharper SDK) at type-load time.
internal static class IlSpyExternalSourcesProviderHelpers
{
    /// <summary>
    /// Picks the initial <see cref="SourceLinkOutcome"/> for a fetch given the
    /// user's PreferSourceLink toggle and the output mode. Determines the
    /// branch the fetch *will* take before any HTTP work happens, so the
    /// banner can explain why SourceLink wasn't used (Disabled / SkippedMode /
    /// NotAttempted) without re-deriving the choice later.
    /// </summary>
    /// <remarks>
    /// Pure (no SDK deps), so it lives here rather than on the provider — that
    /// also lets the choice be unit-tested in isolation from the orchestration
    /// machinery that consumes it.
    /// </remarks>
    public static SourceLinkOutcome InitialSourceLinkOutcome(bool preferSourceLink, IlSpyOutputMode mode)
    {
        if (!preferSourceLink) return SourceLinkOutcome.Plain(SourceLinkStatus.Disabled);
        if (mode != IlSpyOutputMode.CSharp) return SourceLinkOutcome.Plain(SourceLinkStatus.SkippedMode);
        return SourceLinkOutcome.Plain(SourceLinkStatus.NotAttempted);
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is a reference-only assembly file,
    /// recognized by the canonical <c>/ref/</c> / <c>\ref\</c> / <c>.ref/</c>
    /// path segments emitted by the .NET SDK. Reference assemblies carry no IL
    /// bodies — decompiling one yields empty-body stubs — so callers prefer the
    /// implementation file over any ref candidate.
    /// </summary>
    /// <remarks>
    /// Pure string predicate extracted from <c>IlSpyExternalSourcesProvider.ResolveAssemblyFile</c>
    /// so the heuristic can be regression-tested without spinning up an
    /// IAssembly (which requires the full ReSharper SDK). The wider
    /// ResolveAssemblyFile still lives on the provider because it walks
    /// IAssembly.GetFiles() — the per-path predicate is the only purely
    /// algorithmic piece worth pinning by test.
    /// </remarks>
    public static bool IsRefAssemblyPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return false;
        return fullPath.Contains("/ref/") || fullPath.Contains("\\ref\\") || fullPath.Contains(".ref/");
    }

    /// <summary>
    /// Picks the best on-disk impl candidate from a sequence of raw path
    /// strings (typically HintPaths read from csproj <c>&lt;Reference&gt;</c>
    /// items via <c>IProjectToAssemblyReference.ReferenceTarget.HintLocation</c>).
    /// Always prefers an existing non-ref candidate. When no impl exists, the
    /// fallback depends on <paramref name="allowRefAssemblies"/>:
    /// <list type="bullet">
    ///   <item><description>true (default): returns the first existing ref candidate so the navigation pipeline can still surface SOMETHING.</description></item>
    ///   <item><description>false: returns null, letting the caller defer the navigation to a peer provider (e.g. Rider's built-in dotPeek) rather than emit ILSpy's empty-body stub output for a ref-only assembly.</description></item>
    /// </list>
    /// Returns null when nothing exists on disk regardless of the flag.
    /// </summary>
    /// <remarks>
    /// Extracted as a pure helper so the impl-preferred-over-ref selection
    /// logic from <c>IlSpyExternalSourcesProvider.ResolveAssemblyFile</c> can
    /// be regression-tested without standing up a JetBrains IAssembly. Takes
    /// raw strings (not FileSystemPath) so the test can use a file-existence
    /// callback and avoid touching the disk. The <paramref name="allowRefAssemblies"/>
    /// default preserves the pre-setting behavior for any caller that hasn't
    /// been threaded with the user's <c>DecompileReferenceAssemblies</c> toggle.
    /// </remarks>
    public static string? PickImplPathFromCandidates(
        IEnumerable<string?> candidatePaths,
        Func<string, bool> fileExists,
        bool allowRefAssemblies = true)
    {
        if (candidatePaths == null) return null;
        string? firstExistingRef = null;
        foreach (string? raw in candidatePaths)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            if (!fileExists(raw!)) continue;
            if (!IsRefAssemblyPath(raw!)) return raw;
            if (firstExistingRef == null) firstExistingRef = raw;
        }
        return allowRefAssemblies ? firstExistingRef : null;
    }

    /// <summary>
    /// Parses the assembly short name out of a JetBrains <c>IAssembly.FullAssemblyName</c>
    /// (the standard <c>Name, Version=v, Culture=c, PublicKeyToken=t</c> identity string).
    /// Returns null when the input is empty or unparseable so callers can short-circuit
    /// the csproj-wildcard pass without an exception.
    /// </summary>
    /// <remarks>
    /// Pure helper extracted so the parse is unit-testable without standing up an
    /// IAssembly. Uses <see cref="System.Reflection.AssemblyName"/> rather than a
    /// hand-rolled comma split so any future identity-string evolution (versioned
    /// suffixes, ContentType, etc.) tracks the BCL parser instead of needing a
    /// custom regex.
    /// </remarks>
    public static string? TryParseAssemblyShortName(string? fullAssemblyName)
    {
        if (string.IsNullOrEmpty(fullAssemblyName)) return null;
        try
        {
            string? name = new System.Reflection.AssemblyName(fullAssemblyName!).Name;
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <paramref name="csprojPath"/> as XML, walks every <c>&lt;Reference&gt;</c>
    /// item (regardless of MSBuild XML namespace), and returns the absolute filesystem
    /// paths whose basename matches <paramref name="assemblyShortName"/><c>.dll</c>
    /// (case-insensitive). Resolves both the <c>Include</c> attribute and the nested
    /// <c>&lt;HintPath&gt;</c> child as path candidates, expands a single-segment
    /// wildcard (<c>*</c> / <c>?</c>) in the file portion via <paramref name="enumerateFiles"/>,
    /// and silently skips entries that contain unresolved MSBuild variables
    /// (<c>$(...)</c> / <c>%(...)</c>) since this helper does not evaluate them.
    /// </summary>
    /// <remarks>
    /// Exists because <c>IModuleReferencesResolveStore.GetReferencesToAssemblyForAllContexts</c>
    /// only surfaces MSBuild's deduped resolved view of the references — a single
    /// reference per assembly identity, which means when a project's csproj points at
    /// both a publicised ref pack (NuGet) and a local impl directory (e.g. game install)
    /// via wildcard <c>Include="path/*.dll"</c> items, only the NuGet ref reaches
    /// Rider's resolved reference graph. Decompiling that ref yields empty-body stubs.
    /// Going back to the raw csproj XML is the only way to find the impl candidate
    /// that the user explicitly listed but that MSBuild collapsed away.
    ///
    /// Takes IO seams (<paramref name="readAllText"/>, <paramref name="enumerateFiles"/>)
    /// so tests can drive the parse without touching the filesystem.
    /// </remarks>
    public static IReadOnlyList<string> EnumerateCsprojReferenceCandidates(
        string csprojPath,
        string assemblyShortName,
        Func<string, string> readAllText,
        Func<string, string, IEnumerable<string>> enumerateFiles)
    {
        if (string.IsNullOrEmpty(csprojPath) || string.IsNullOrEmpty(assemblyShortName))
            return Array.Empty<string>();

        string xml;
        try
        {
            xml = readAllText(csprojPath);
        }
        catch
        {
            return Array.Empty<string>();
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            return Array.Empty<string>();
        }

        string csprojDir = Path.GetDirectoryName(csprojPath) ?? string.Empty;
        string expectedFileName = assemblyShortName + ".dll";
        List<string> matches = new List<string>();

        foreach (XElement referenceElement in doc.Descendants().Where(e => e.Name.LocalName == "Reference"))
        {
            foreach (string rawPath in ExtractReferencePathCandidates(referenceElement))
            {
                if (ContainsUnresolvedMsbuildVariable(rawPath)) continue;
                string absolute = MakeAbsoluteRelativeTo(csprojDir, rawPath);
                foreach (string match in ResolveCandidateToExistingFiles(absolute, expectedFileName, enumerateFiles))
                {
                    matches.Add(match);
                }
            }
        }
        return matches;
    }

    private static IEnumerable<string> ExtractReferencePathCandidates(XElement referenceElement)
    {
        string? include = referenceElement.Attribute("Include")?.Value;
        if (LooksLikeFilePath(include))
            yield return include!;

        foreach (XElement child in referenceElement.Elements())
        {
            if (child.Name.LocalName != "HintPath") continue;
            string? value = child.Value;
            if (!string.IsNullOrEmpty(value)) yield return value!;
        }
    }

    private static bool LooksLikeFilePath(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s!.Contains('/') || s.Contains('\\')
            || s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || s.Contains('*') || s.Contains('?');
    }

    private static bool ContainsUnresolvedMsbuildVariable(string path)
        => path.Contains("$(") || path.Contains("%(");

    private static string MakeAbsoluteRelativeTo(string baseDir, string maybeRelative)
    {
        if (Path.IsPathRooted(maybeRelative)) return maybeRelative;
        if (string.IsNullOrEmpty(baseDir)) return maybeRelative;
        try { return Path.GetFullPath(Path.Combine(baseDir, maybeRelative)); }
        catch { return maybeRelative; }
    }

    private static IReadOnlyList<string> ResolveCandidateToExistingFiles(
        string absolutePath,
        string expectedFileName,
        Func<string, string, IEnumerable<string>> enumerateFiles)
    {
        if (string.IsNullOrEmpty(absolutePath)) return Array.Empty<string>();
        string dir = Path.GetDirectoryName(absolutePath) ?? string.Empty;
        string filePart = Path.GetFileName(absolutePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(filePart)) return Array.Empty<string>();

        if (filePart.Contains('*') || filePart.Contains('?'))
        {
            // Override the user's glob to the specific expected file so a
            // wildcard like "*.dll" doesn't return hundreds of unrelated DLLs.
            try
            {
                return enumerateFiles(dir, expectedFileName).ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        if (string.Equals(filePart, expectedFileName, StringComparison.OrdinalIgnoreCase))
            return new[] { absolutePath };

        return Array.Empty<string>();
    }

    /// <summary>
    /// Splits a delimited <paramref name="raw"/> setting value into a list of canonical
    /// absolute directory paths, warning (via <paramref name="warn"/>) for each rejected
    /// entry. Uses <see cref="Path.PathSeparator"/> — ';' on Windows, ':' on Linux/macOS —
    /// so the setting string mirrors the platform's PATH convention. Returns an empty
    /// list for null/empty/whitespace input.
    /// </summary>
    /// <remarks>
    /// Extracted out of <c>IlSpyRequestSettingsBuilder.GetExtraSearchDirs</c> so the
    /// split-and-accumulate pattern is directly unit-testable without spinning up the
    /// ReSharper SDK. The previous inline implementation split only on ';', which
    /// silently dropped Linux/macOS users' colon-separated lists despite the setting's
    /// comment claiming both delimiters were supported.
    /// </remarks>
    public static IReadOnlyList<string> ParseExtraSearchDirs(string? raw, Action<string> warn)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        string[] parts = raw!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (TryNormalizeSearchDir(part, out string canonical, out string? rejection))
            {
                result.Add(canonical);
            }
            else if (rejection != null)
            {
                warn(rejection);
            }
        }
        return result;
    }

    public static bool TryNormalizeSearchDir(string raw, out string canonical, out string? rejection)
    {
        canonical = string.Empty;
        rejection = null;

        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return false;

        if (trimmed.StartsWith("\\\\") || trimmed.StartsWith("//"))
        {
            rejection = "RiderIlSpy: rejecting UNC/network search dir: " + trimmed;
            return false;
        }
        if (!Path.IsPathRooted(trimmed))
        {
            rejection = "RiderIlSpy: rejecting non-absolute search dir: " + trimmed;
            return false;
        }

        try
        {
            canonical = Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            rejection = "RiderIlSpy: rejecting unresolvable search dir '" + trimmed + "': " + ex.Message;
            return false;
        }

        if (!Directory.Exists(canonical))
        {
            rejection = "RiderIlSpy: search dir does not exist: " + canonical;
            canonical = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Inverse of <see cref="BuildCacheProperties"/>. Returns null when any
    /// required key is missing or the mode is unparseable. Kept SDK-free so the
    /// property-bag-to-fields parsing is unit-testable without spinning up an
    /// <see cref="JetBrains.Metadata.Reader.API.IAssembly"/>; the provider layer
    /// adds the IAssembly handle on top of this pure parse.
    /// </summary>
    public static DecompileEntryFields? TryParseDecompileEntryFields(IDictionary<string, string>? properties)
    {
        if (properties == null) return null;
        if (!properties.TryGetValue("RiderIlSpy.Moniker", out string? moniker) || string.IsNullOrEmpty(moniker)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Assembly", out string? asmPath)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Type", out string? typeFullName)) return null;
        if (!properties.TryGetValue("RiderIlSpy.FileName", out string? fileName)) return null;
        if (!properties.TryGetValue("RiderIlSpy.Mode", out string? modeStr)) return null;
        if (!Enum.TryParse(modeStr, out IlSpyOutputMode mode)) return null;
        return new DecompileEntryFields(asmPath, typeFullName, moniker, fileName, mode);
    }

    // Returns Dictionary<,> (mutable) rather than IReadOnlyDictionary<,> because
    // the sole production caller is Rider's PutCacheItem, which takes
    // IDictionary<,>. Returning the concrete type avoids a downcast at the
    // API boundary; tests still consume it through the IReadOnly view implicitly.
    public static Dictionary<string, string> BuildCacheProperties(IlSpyOutputMode mode, string assemblyPath, string typeFullName, string moniker, string fileName)
    {
        return new Dictionary<string, string>
        {
            ["RiderIlSpy.Mode"] = mode.ToString(),
            ["RiderIlSpy.Assembly"] = assemblyPath,
            ["RiderIlSpy.Type"] = typeFullName,
            ["RiderIlSpy.Moniker"] = moniker,
            ["RiderIlSpy.FileName"] = fileName,
        };
    }

    // Production callers use the overload that reads HOME from the environment;
    // unit tests use the explicit-home overload to avoid mutating the process-wide
    // HOME env var (which would break under xunit parallelism).
    public static string RedactHome(string path) =>
        RedactHome(path, Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string RedactHome(string path, string? homeDir)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (string.IsNullOrEmpty(homeDir)) return path;
        if (path.StartsWith(homeDir, StringComparison.OrdinalIgnoreCase))
            return "~" + path.Substring(homeDir.Length);
        return path;
    }

    // Convenience seam so both decompile paths use a single point for banner prepending.
    // Fetching AssemblyBannerMetadata stays at the caller because it depends on
    // IlSpyDecompiler (a ReSharper SDK component); this helper itself is pure.
    public static string WithBannerIfEnabled(bool showBanner, BannerContext ctx, string content)
        => WithBannerIfEnabled(showBanner, ctx, sourceLinkOutcome: null, content);

    /// <summary>
    /// Banner overload that surfaces a SourceLink fetch outcome row. When the
    /// outcome is non-null and emits a visible banner line (silent statuses like
    /// Disabled / SkippedMode / NotAttempted return null from the formatter),
    /// the banner gets an extra <c>// SourceLink: &lt;status&gt;</c> line so
    /// users can tell why the original source wasn't used without grepping idea.log.
    /// </summary>
    public static string WithBannerIfEnabled(bool showBanner, BannerContext ctx, SourceLinkOutcome? sourceLinkOutcome, string content)
    {
        if (!showBanner) return content;
        return BuildDiagnosticBanner(ctx, sourceLinkOutcome) + content;
    }

    // Mirrors the JetBrains decompiler banner shape so output is visually familiar:
    //   // Decompiled with RiderIlSpy (ICSharpCode.Decompiler 8.2.0)
    //   // Type: <fqn>
    //   // Assembly: <name>, Version=<v>, Culture=<c>, PublicKeyToken=<t>
    //   // MVID: <guid>
    //   // Target framework: <tfm>
    //   // File size: <bytes>
    //   // Assembly location: <redacted path>
    //   // XML documentation location: <path or (none)>
    //   // Mode: <CSharp|IL|CSharpWithIL>
    //   // Extra search dirs: <(none) or comma list>
    // Reading metadata is best-effort: if the caller could not retrieve it (null)
    // we still emit the path/mode rows so the banner remains useful (e.g. for
    // diagnosing assembly resolution).
    public static string BuildDiagnosticBanner(BannerContext ctx, SourceLinkOutcome? sourceLinkOutcome)
    {
        string? sidecar = IlSpyXmlDocResolver.GetSidecarXmlDocPath(ctx.AssemblyPath);
        bool sidecarExists = !string.IsNullOrEmpty(sidecar) && File.Exists(sidecar);
        string? refPackDir = IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(ctx.AssemblyPath);
        int refPackFileCount = 0;
        if (refPackDir != null && Directory.Exists(refPackDir))
        {
            try { refPackFileCount = Directory.GetFiles(refPackDir, "*.xml", SearchOption.AllDirectories).Length; }
            catch { /* enumeration failure is non-fatal; banner just omits the ref-pack line */ }
        }

        StringBuilder sb = new StringBuilder(512);
        sb.Append("// Decompiled with RiderIlSpy (ICSharpCode.Decompiler ").Append(ctx.DecompilerVersion).Append(")\n");
        sb.Append("// Type: ").Append(ctx.TypeFullName).Append('\n');
        if (ctx.Meta != null)
        {
            sb.Append("// Assembly: ").Append(ctx.Meta.Name)
              .Append(", Version=").Append(ctx.Meta.Version)
              .Append(", Culture=").Append(ctx.Meta.Culture)
              .Append(", PublicKeyToken=").Append(ctx.Meta.PublicKeyToken).Append('\n');
            sb.Append("// MVID: ").Append(ctx.Meta.Mvid).Append('\n');
            sb.Append("// Target framework: ").Append(ctx.Meta.TargetFramework).Append('\n');
            sb.Append("// File size: ").Append(ctx.Meta.FileSize.ToString("N0", CultureInfo.InvariantCulture)).Append(" bytes\n");
        }
        sb.Append("// Assembly location: ").Append(RedactHome(ctx.AssemblyPath)).Append('\n');
        sb.Append("// XML documentation location: ").Append(sidecarExists ? RedactHome(sidecar!) : "(none)").Append('\n');
        // Ref-pack line is conditional: only .NET shared-runtime impl assemblies
        // get a hit, so emitting "(none)" here for every NuGet decompile would
        // be visual noise. Single-noun "file" / "files" pluralization just
        // because the count can be 1 (yes, really — a ref pack with one xml).
        if (refPackFileCount > 0)
        {
            sb.Append("// XML documentation ref pack: ").Append(RedactHome(refPackDir!))
              .Append(" (").Append(refPackFileCount).Append(refPackFileCount == 1 ? " file)" : " files)")
              .Append('\n');
        }
        sb.Append("// Mode: ").Append(ctx.Mode).Append('\n');
        sb.Append("// Extra search dirs: ")
          .Append(ctx.ExtraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", ctx.ExtraSearchDirs.Select(RedactHome)))
          .Append('\n');
        // SourceLink line: only emitted for outcomes that answer "why did
        // SourceLink not kick in?" usefully. Disabled / SkippedMode / NotAttempted
        // are silenced via the formatter — the user can read the Mode line and
        // the settings toggle and infer the rest. NoPdb, NoSourceLinkEntry,
        // HttpFailed, Used, etc. each carry a unique diagnostic so they're shown.
        if (sourceLinkOutcome != null)
        {
            string? line = SourceLinkOutcomeFormatter.FormatBannerLine(sourceLinkOutcome);
            if (line != null)
                sb.Append(line).Append('\n');
        }
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Number of source lines a prepended banner string occupies. The banner is
    /// glued in front of the decompiled text verbatim (<c>banner + content</c>),
    /// so every <c>\n</c> in the banner pushes the real source down by one line.
    /// Returns 0 for a null/empty banner (banner disabled). Used to shift
    /// sequence-point line numbers — which ICSharpCode.Decompiler emits relative
    /// to the un-bannered syntax tree — onto the lines the user actually sees.
    /// </summary>
    public static int CountBannerLines(string? banner)
    {
        if (string.IsNullOrEmpty(banner)) return 0;
        int count = 0;
        foreach (char c in banner!)
            if (c == '\n') count++;
        return count;
    }

    /// <summary>
    /// Shifts every non-hidden sequence point's start/end line down by
    /// <paramref name="bannerLineCount"/> so the points line up with the
    /// banner-prefixed text the debugger will read from the cache. Hidden points
    /// (the portable-PDB 0xFEEFEE sentinel) carry no real source region, so they
    /// pass through untouched. Columns and IL offsets are never touched — the
    /// banner only adds whole lines. Returns the input unchanged when there is no
    /// banner (offset &lt;= 0) or no methods, so the common no-banner path
    /// allocates nothing.
    /// </summary>
    public static IReadOnlyList<MethodSequencePoints> OffsetSequencePointsByBannerLines(
        IReadOnlyList<MethodSequencePoints> methods, int bannerLineCount)
    {
        if (bannerLineCount <= 0 || methods.Count == 0) return methods;
        List<MethodSequencePoints> shiftedMethods = new List<MethodSequencePoints>(methods.Count);
        foreach (MethodSequencePoints method in methods)
        {
            List<IlSpySequencePoint> shiftedPoints = new List<IlSpySequencePoint>(method.Points.Count);
            foreach (IlSpySequencePoint point in method.Points)
            {
                shiftedPoints.Add(point.IsHidden
                    ? point
                    : point with
                    {
                        StartLine = point.StartLine + bannerLineCount,
                        EndLine = point.EndLine + bannerLineCount,
                    });
            }
            shiftedMethods.Add(method with { Points = shiftedPoints });
        }
        return shiftedMethods;
    }

    /// <summary>
    /// Whether an Enabled-flag change should trigger eviction of the entries we
    /// wrote under Rider's shared "decompiler" cache id. True only on the
    /// enable->disable edge (<paramref name="wasEnabled"/> true,
    /// <paramref name="nowEnabled"/> false): that is the moment dotPeek takes
    /// back ownership of navigation and would otherwise serve our stale ILSpy
    /// output. Same-value fires (including the initial advise replay of the
    /// default-true value) and the disable->enable edge return false — on
    /// re-enable our provider wins navigation again and re-decompiles on demand,
    /// so there's nothing to evict.
    /// </summary>
    public static bool ShouldEvictOnEnabledChange(bool wasEnabled, bool nowEnabled)
        => wasEnabled && !nowEnabled;
}
