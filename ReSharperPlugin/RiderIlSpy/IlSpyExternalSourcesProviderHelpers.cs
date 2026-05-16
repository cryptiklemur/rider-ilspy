using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace RiderIlSpy;

// Pure helpers separated from IlSpyExternalSourcesProvider so unit tests can
// exercise them without dragging in JetBrains.ReSharper.Feature.Services (and
// the rest of the ReSharper SDK) at type-load time.
internal static class IlSpyExternalSourcesProviderHelpers
{
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

    // Banner enrichers swallow exceptions without logging because (a) these
    // helpers are deliberately ReSharper-SDK-free (so tests can load them) and
    // pulling in JetBrains.Util.Logging here would defeat that, and (b) every
    // failure path returns a sentinel ("", "unknown") that the banner already
    // renders meaningfully — losing version/xml-path enrichment is non-fatal
    // diagnostic noise, not data loss.
    public static string XmlDocPathOrEmpty(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath)) return string.Empty;
        try { return Path.ChangeExtension(assemblyPath, ".xml"); }
        catch { /* non-fatal: banner degrades gracefully to "(none)" */ return string.Empty; }
    }

    public static string GetDecompilerVersion()
    {
        try
        {
            Version? v = typeof(CSharpDecompiler).Assembly.GetName().Version;
            return v?.ToString(3) ?? "unknown";
        }
        catch
        {
            /* non-fatal: banner shows "unknown" instead of the version string */
            return "unknown";
        }
    }

    // Pure assembly-identity reader extracted from IlSpyDecompiler so unit tests
    // can hit it without dragging in JetBrains.Util.Logging at JIT time. The
    // wrapper on IlSpyDecompiler adds Warn-on-failure logging; this helper just
    // returns null. ECMA-335 II.6.3 governs the public key token derivation.
    public static AssemblyBannerMetadata? ReadAssemblyBannerMetadata(string assemblyPath)
    {
        try
        {
            using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
            MetadataReader metadata = module.Metadata;
            if (!metadata.IsAssembly) return null;

            AssemblyDefinition def = metadata.GetAssemblyDefinition();
            string name = metadata.GetString(def.Name);
            string version = def.Version?.ToString() ?? "0.0.0.0";
            string culture = metadata.GetString(def.Culture);
            if (string.IsNullOrEmpty(culture)) culture = "neutral";

            byte[] publicKey = def.PublicKey.IsNil ? Array.Empty<byte>() : metadata.GetBlobBytes(def.PublicKey);
            string publicKeyToken = publicKey.Length == 0 ? "null" : ComputePublicKeyToken(publicKey);

            ModuleDefinition modDef = metadata.GetModuleDefinition();
            string mvid = metadata.GetGuid(modDef.Mvid).ToString("D").ToUpperInvariant();

            long fileSize = 0L;
            try { fileSize = new FileInfo(assemblyPath).Length; } catch { /* unreadable size is non-fatal */ }

            string targetFramework = "unknown";
            try { targetFramework = module.DetectTargetFrameworkId() ?? "unknown"; } catch { /* missing TFM is non-fatal */ }

            return new AssemblyBannerMetadata(name, version, culture, publicKeyToken, mvid, fileSize, targetFramework);
        }
        catch
        {
            /* non-fatal: callers log via their own logger; helper stays SDK-free */
            return null;
        }
    }

    internal static string ComputePublicKeyToken(byte[] publicKey)
    {
        using SHA1 sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(publicKey);
        StringBuilder sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(hash[hash.Length - 1 - i].ToString("x2"));
        return sb.ToString();
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
        string xmlDocPath = XmlDocPathOrEmpty(ctx.AssemblyPath);
        bool xmlExists = !string.IsNullOrEmpty(xmlDocPath) && File.Exists(xmlDocPath);

        StringBuilder sb = new StringBuilder(512);
        sb.Append("// Decompiled with RiderIlSpy (ICSharpCode.Decompiler ").Append(GetDecompilerVersion()).Append(")\n");
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
        sb.Append("// XML documentation location: ").Append(xmlExists ? RedactHome(xmlDocPath) : "(none)").Append('\n');
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
}
