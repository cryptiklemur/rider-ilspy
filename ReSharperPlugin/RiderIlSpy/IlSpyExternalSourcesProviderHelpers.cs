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

    public static IDictionary<string, string> BuildCacheProperties(IlSpyOutputMode mode, string assemblyPath, string typeFullName, string moniker, string fileName)
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

    public static string RedactHome(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return path;
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path.Substring(home.Length);
        return path;
    }

    // Banner enrichers swallow exceptions without logging because (a) these
    // helpers are deliberately ReSharper-SDK-free (so tests can load them) and
    // pulling in JetBrains.Util.Logging here would defeat that, and (b) every
    // failure path returns a sentinel ("", "unknown") that the banner already
    // renders meaningfully — losing version/xml-path enrichment is non-fatal
    // diagnostic noise, not data loss.
    public static string SafeXmlDocPath(string assemblyPath)
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
    public static string WithBannerIfEnabled(bool showBanner, AssemblyBannerMetadata? meta, string assemblyPath, string typeFullName, IlSpyOutputMode mode, IReadOnlyList<string> extraSearchDirs, string content)
    {
        if (!showBanner) return content;
        return BuildDiagnosticBanner(meta, assemblyPath, typeFullName, mode, extraSearchDirs) + content;
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
    public static string BuildDiagnosticBanner(AssemblyBannerMetadata? meta, string assemblyPath, string typeFullName, IlSpyOutputMode mode, IReadOnlyList<string> extraSearchDirs)
    {
        string xmlDocPath = SafeXmlDocPath(assemblyPath);
        bool xmlExists = !string.IsNullOrEmpty(xmlDocPath) && File.Exists(xmlDocPath);

        StringBuilder sb = new StringBuilder(512);
        sb.Append("// Decompiled with RiderIlSpy (ICSharpCode.Decompiler ").Append(GetDecompilerVersion()).Append(")\n");
        sb.Append("// Type: ").Append(typeFullName).Append('\n');
        if (meta != null)
        {
            sb.Append("// Assembly: ").Append(meta.Name)
              .Append(", Version=").Append(meta.Version)
              .Append(", Culture=").Append(meta.Culture)
              .Append(", PublicKeyToken=").Append(meta.PublicKeyToken).Append('\n');
            sb.Append("// MVID: ").Append(meta.Mvid).Append('\n');
            sb.Append("// Target framework: ").Append(meta.TargetFramework).Append('\n');
            sb.Append("// File size: ").Append(meta.FileSize.ToString("N0", CultureInfo.InvariantCulture)).Append(" bytes\n");
        }
        sb.Append("// Assembly location: ").Append(RedactHome(assemblyPath)).Append('\n');
        sb.Append("// XML documentation location: ").Append(xmlExists ? RedactHome(xmlDocPath) : "(none)").Append('\n');
        sb.Append("// Mode: ").Append(mode).Append('\n');
        sb.Append("// Extra search dirs: ")
          .Append(extraSearchDirs.Count == 0 ? "(none)" : string.Join(", ", extraSearchDirs.Select(RedactHome)))
          .Append("\n\n");
        return sb.ToString();
    }
}
