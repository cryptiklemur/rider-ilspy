using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace RiderIlSpy;

/// <summary>
/// Engine-side assembly identity readers extracted from
/// IlSpyExternalSourcesProviderHelpers: these touch ICSharpCode.Decompiler and
/// System.Reflection.Metadata, so they live behind the load-context boundary
/// and surface only Contracts types (AssemblyBannerMetadata, string).
/// </summary>
public static class AssemblyBannerReader
{
    // Banner enrichers swallow exceptions without logging because (a) these
    // helpers are deliberately ReSharper-SDK-free (so tests can load them) and
    // pulling in JetBrains.Util.Logging here would defeat that, and (b) every
    // failure path returns a sentinel ("", "unknown") that the banner already
    // renders meaningfully — losing version/xml-path enrichment is non-fatal
    // diagnostic noise, not data loss.
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
}
