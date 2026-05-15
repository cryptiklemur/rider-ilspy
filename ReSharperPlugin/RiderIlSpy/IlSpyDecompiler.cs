using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using JetBrains.Application;

namespace RiderIlSpy;

/// <summary>
/// Identity metadata read directly from an assembly's CLI header — used by the
/// diagnostic banner. Mirrors the fields surfaced by the JetBrains decompiler banner
/// (Assembly identity, MVID, target framework, file size).
/// </summary>
/// <param name="Name">Simple assembly name (no version, no culture suffix).</param>
/// <param name="Version">Four-part assembly version string.</param>
/// <param name="Culture">Culture name; "neutral" when unset.</param>
/// <param name="PublicKeyToken">Lowercase hex token computed from the SHA1 of the
/// public key per ECMA-335 II.6.3, or "null" for unsigned assemblies.</param>
/// <param name="Mvid">Module Version Id, uppercased "D" Guid format.</param>
/// <param name="FileSize">Length of the PE file on disk, in bytes; 0 if unreadable.</param>
/// <param name="TargetFramework">TFM moniker (e.g. ".NETCoreApp,Version=v8.0"); "unknown" if absent.</param>
public sealed record AssemblyBannerMetadata(
    string Name,
    string Version,
    string Culture,
    string PublicKeyToken,
    string Mvid,
    long FileSize,
    string TargetFramework);

[ShellComponent]
public class IlSpyDecompiler
{
    public string DecompileType(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs = null, IlSpyOutputMode mode = IlSpyOutputMode.CSharp)
    {
        try
        {
            switch (mode)
            {
                case IlSpyOutputMode.IL:
                    return DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs);
                case IlSpyOutputMode.CSharpWithIL:
                    return DisassembleMixed(assemblyPath, typeFullName, settings, extraSearchDirs);
                default:
                    return DecompileToCSharp(assemblyPath, typeFullName, settings, extraSearchDirs);
            }
        }
        catch (System.Exception ex)
        {
            return FormatDecompileFailure(typeFullName, ex);
        }
    }

    // Wraps an exception thrown out of ICSharpCode.Decompiler into a comment-only C# file
    // that Rider can display. Includes the full exception chain + stack trace so the user
    // can copy/paste it into a bug report without us needing a second round-trip.
    private static string FormatDecompileFailure(string typeFullName, System.Exception ex)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("// RiderIlSpy decompile failed for ").Append(typeFullName).Append('\n');
        sb.Append("//\n");
        sb.Append("// This is almost always an ICSharpCode.Decompiler bug. Please file an issue at\n");
        sb.Append("// https://github.com/cryptiklemur/rider-ilspy/issues with the type name and the\n");
        sb.Append("// trace below.\n");
        sb.Append("//\n");
        System.Exception? current = ex;
        int depth = 0;
        while (current != null)
        {
            string indent = depth == 0 ? "// " : "//   ";
            sb.Append(indent).Append(current.GetType().FullName).Append(": ").Append(current.Message).Append('\n');
            if (!string.IsNullOrEmpty(current.StackTrace))
                foreach (string line in current.StackTrace.Split('\n'))
                    sb.Append("//     ").Append(line.TrimEnd('\r')).Append('\n');
            current = current.InnerException;
            depth++;
        }
        return sb.ToString();
    }

    public string DecompileAssemblyInfo(string assemblyPath, DecompilerSettings? settings = null, IReadOnlyList<string>? extraSearchDirs = null)
    {
        DecompilerSettings effective = settings ?? new DecompilerSettings();
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, effective, extraSearchDirs);
        CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, effective);
        return decompiler.DecompileModuleAndAssemblyAttributesToString();
    }

    /// <summary>
    /// Reads identity metadata from a PE/CLI assembly without loading it into the
    /// AppDomain. Returns null when the file is unreadable or not a managed assembly.
    /// Used by the diagnostic banner to mirror the JetBrains decompiler's
    /// `// Assembly: ... // MVID: ...` header.
    /// </summary>
    public AssemblyBannerMetadata? GetAssemblyBannerMetadata(string assemblyPath)
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

            byte[] publicKey = def.PublicKey.IsNil ? System.Array.Empty<byte>() : metadata.GetBlobBytes(def.PublicKey);
            string publicKeyToken = publicKey.Length == 0
                ? "null"
                : ComputePublicKeyToken(publicKey);

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
            return null;
        }
    }

    // ECMA-335 II.6.3: the public key token is the last 8 bytes of the SHA1 hash of
    // the public key, in reverse order. Strong-named assemblies store the full key in
    // the AssemblyDef row; unsigned assemblies store nothing.
    private static string ComputePublicKeyToken(byte[] publicKey)
    {
        using SHA1 sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(publicKey);
        StringBuilder sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(hash[hash.Length - 1 - i].ToString("x2"));
        return sb.ToString();
    }

    private static string DecompileToCSharp(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);
        CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, settings);

        FullTypeName ftn;
        try { ftn = new FullTypeName(typeFullName); }
        catch { return "// invalid type name: " + typeFullName; }

        ITypeDefinition? type = decompiler.TypeSystem.MainModule.GetTypeDefinition(ftn);
        if (type == null) return "// type not found: " + typeFullName;
        return decompiler.DecompileTypeAsString(ftn);
    }

    private static string DisassembleMixed(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        using StringWriter sw = new StringWriter();
        PlainTextOutput output = new PlainTextOutput(sw);
        MixedMethodBodyDisassembler bodyDisassembler = new MixedMethodBodyDisassembler(output, CancellationToken.None, settings, resolver)
        {
            DetectControlStructure = true,
            ShowSequencePoints = false,
        };
        ReflectionDisassembler disassembler = new ReflectionDisassembler(output, bodyDisassembler, CancellationToken.None)
        {
            AssemblyResolver = resolver,
            DetectControlStructure = true,
            ShowSequencePoints = false,
            ExpandMemberDefinitions = true,
        };
        disassembler.DisassembleType(module, handle);
        return sw.ToString();
    }

    private static string DisassembleToIl(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        using StringWriter sw = new StringWriter();
        PlainTextOutput output = new PlainTextOutput(sw);
        ReflectionDisassembler disassembler = new ReflectionDisassembler(output, CancellationToken.None)
        {
            AssemblyResolver = resolver,
            DetectControlStructure = true,
            ShowSequencePoints = false,
            ExpandMemberDefinitions = true,
        };
        disassembler.DisassembleType(module, handle);
        return sw.ToString();
    }

    private static UniversalAssemblyResolver BuildResolver(string assemblyPath, PEFile module, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
            assemblyPath,
            settings.ThrowOnAssemblyResolveErrors,
            module.DetectTargetFrameworkId());
        if (extraSearchDirs != null)
            foreach (string dir in extraSearchDirs)
                resolver.AddSearchDirectory(dir);
        return resolver;
    }

    private static TypeDefinitionHandle FindTypeHandle(PEFile module, string typeFullName)
    {
        MetadataReader metadata = module.Metadata;
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition def = metadata.GetTypeDefinition(handle);
            string built = BuildTypeFullName(metadata, def);
            if (built == typeFullName) return handle;
        }
        return default;
    }

    // Builds a CLR-reflection-style full name: `Namespace.Outer+Inner+Leaf` with generic arity
    // baked into each segment (`List`1`). Rider's IClrTypeName.FullName uses the same shape.
    private static string BuildTypeFullName(MetadataReader metadata, TypeDefinition def)
    {
        string name = metadata.GetString(def.Name);
        TypeDefinitionHandle declaringHandle = def.GetDeclaringType();
        if (!declaringHandle.IsNil)
        {
            TypeDefinition decl = metadata.GetTypeDefinition(declaringHandle);
            return BuildTypeFullName(metadata, decl) + "+" + name;
        }
        string ns = metadata.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
