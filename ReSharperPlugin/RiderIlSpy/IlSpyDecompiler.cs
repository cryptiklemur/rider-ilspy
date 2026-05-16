using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using JetBrains.Application;
using JetBrains.Util;
using JetBrains.Util.Logging;

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

/// <summary>
/// Result of <see cref="IlSpyDecompiler.DecompileAssemblyToProject"/>. Surfaces the directory ILSpy
/// wrote into plus a couple of summary counts so the caller can show a "wrote N files, project at X"
/// confirmation without re-enumerating the output tree.
/// </summary>
/// <param name="OutputDirectory">Absolute path that was passed as the project root.</param>
/// <param name="ProjectFilePath">Absolute path to the generated .csproj, or null if ILSpy
/// emitted source without a project (rare — happens for module-only assemblies).</param>
/// <param name="CSharpFileCount">Number of .cs files written under <paramref name="OutputDirectory"/>.</param>
public sealed record DecompileAssemblyToProjectResult(
    string OutputDirectory,
    string? ProjectFilePath,
    int CSharpFileCount);

[ShellComponent]
public class IlSpyDecompiler
{
    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyDecompiler>();

    // Single HttpClient instance reused across SourceLink fetches. HttpClient is
    // documented as designed to be long-lived; creating one per fetch leaks TCP
    // sockets on .NET Core and triggers SocketException after a few thousand
    // requests under sustained navigation.
    private static readonly HttpClient ourSharedHttpClient = CreateSharedHttpClient();

    private static HttpClient CreateSharedHttpClient()
    {
        HttpClient client = new HttpClient();
        // raw.githubusercontent.com (the most common SourceLink target) accepts
        // anonymous GETs without a User-Agent, but other Git hosts (e.g. some
        // self-hosted Gitea) reject empty UAs. Identifying ourselves keeps the
        // fetch unblocked on any vendor's traffic-filter.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RiderIlSpy/1.0 (+https://github.com/cryptiklemur/rider-ilspy)");
        return client;
    }

    /// <summary>
    /// When <paramref name="assemblyPath"/>'s portable PDB carries a SourceLink
    /// CustomDebugInformation entry, looks up <paramref name="typeFullName"/>'s
    /// primary source document, fetches it from the published URL, and returns
    /// its content. Returns <c>null</c> on any failure — caller falls back to
    /// ILSpy decompilation.
    /// </summary>
    /// <remarks>
    /// Returns null when:
    /// - The PDB is missing, non-portable, or unreadable.
    /// - The PDB has no SourceLink entry, or its JSON is malformed.
    /// - The type spans multiple documents (partial class). Picking one of N
    ///   files would surface only part of the type and confuse navigation.
    /// - No mapping rule in the SourceLink JSON covers the document path.
    /// - The HTTP fetch fails (404, timeout, DNS failure, etc.).
    /// </remarks>
    public string? TryGetSourceLinkSource(string assemblyPath, string typeFullName, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds <= 0) timeoutSeconds = 5;
        try
        {
            using PdbSourceLinkReader? pdb = PdbSourceLinkReader.TryOpen(assemblyPath);
            if (pdb == null) return null;
            string? json = pdb.TryReadSourceLinkJson();
            if (json == null) return null;
            SourceLinkMapping? mapping = SourceLinkMapping.TryParse(json);
            if (mapping == null) return null;
            string? documentPath = pdb.TryGetPrimaryDocumentPath(typeFullName);
            if (string.IsNullOrEmpty(documentPath)) return null;
            string? url = mapping.ResolveUrl(documentPath);
            if (string.IsNullOrEmpty(url)) return null;
            string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpy", "sourcelink-cache");
            SourceLinkSourceFetcher fetcher = new SourceLinkSourceFetcher(ourSharedHttpClient, cacheRoot, TimeSpan.FromSeconds(timeoutSeconds));
            return fetcher.FetchOrCached(url, cancellationToken);
        }
        catch (Exception ex)
        {
            // Defensive: we never want a SourceLink lookup to bubble up. Any
            // failure here just means "fall back to decompile" upstream.
            ourLogger.Warn("RiderIlSpy.TryGetSourceLinkSource for " + typeFullName + " threw: " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    public string DecompileType(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs = null, IlSpyOutputMode mode = IlSpyOutputMode.CSharp, bool emitCrosslinkMarkers = true)
    {
        try
        {
            switch (mode)
            {
                case IlSpyOutputMode.IL:
                    return DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs);
                case IlSpyOutputMode.CSharpWithIL:
                    return DisassembleMixed(assemblyPath, typeFullName, settings, extraSearchDirs, emitCrosslinkMarkers);
                default:
                    return DecompileToCSharp(assemblyPath, typeFullName, settings, extraSearchDirs);
            }
        }
        catch (ArgumentException ex) when (mode != IlSpyOutputMode.IL && IsTwoComponentTfmVersionBug(ex))
        {
            // Hit ILSpy's two-component TFM bug. Try to apply the reflection patch
            // (visible side effect in the catch body, not in a `when` filter) and
            // retry. If the patch can't be applied or the retry still fails, fall
            // back to raw IL disassembly.
            if (!NeuterImplicitReferences())
                return FallBackToIl(assemblyPath, typeFullName, settings, extraSearchDirs, ex, null);

            try
            {
                bool wantMixed = mode == IlSpyOutputMode.CSharpWithIL;
                return wantMixed
                    ? DisassembleMixed(assemblyPath, typeFullName, settings, extraSearchDirs, emitCrosslinkMarkers)
                    : DecompileToCSharp(assemblyPath, typeFullName, settings, extraSearchDirs);
            }
            catch (System.Exception retryEx)
            {
                return FallBackToIl(assemblyPath, typeFullName, settings, extraSearchDirs, ex, retryEx);
            }
        }
        catch (System.Exception ex)
        {
            return FormatDecompileFailure(typeFullName, ex);
        }
    }

    // ILSpy's `DecompilerTypeSystem.implicitReferences` is a `static readonly string[]`
    // containing the two assemblies (`System.Runtime.InteropServices` and
    // `System.Runtime.CompilerServices.Unsafe`) that ILSpy tries to inject as implicit
    // references on every .NET Core/.NET 5+ decompile. The injection code calls
    // `tfmVersion.ToString(3)` unconditionally, which throws on .NET 10+ assemblies
    // because `ParseTargetFramework` only pads 2-component versions when the string is
    // exactly 3 chars long (so `v9.0` → padded, `v10.0` → not padded).
    //
    // Swapping the static field to an empty array makes the buggy foreach a no-op for
    // every subsequent decompile in this process. If the type actually needs those
    // assemblies, they're already in its own AssemblyRef table and get resolved normally.
    private static readonly object ourNeuterLock = new object();
    private static bool ourNeuterAttempted;
    private static bool ourNeuterSucceeded;
    private static string? ourNeuterFailureReason;

    private static bool NeuterImplicitReferences() => NeuterImplicitReferences(out _);

    private static bool NeuterImplicitReferences(out string? failureReason)
    {
        lock (ourNeuterLock)
        {
            if (ourNeuterAttempted)
            {
                failureReason = ourNeuterFailureReason;
                return ourNeuterSucceeded;
            }
            ourNeuterAttempted = true;

            try
            {
                Type dts = typeof(DecompilerTypeSystem);
                FieldInfo? field = dts.GetField("implicitReferences", BindingFlags.NonPublic | BindingFlags.Static)
                                ?? dts.GetField("ImplicitReferences", BindingFlags.NonPublic | BindingFlags.Static)
                                ?? dts.GetField("_implicitReferences", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                {
                    ourNeuterFailureReason = "could not find implicitReferences field on DecompilerTypeSystem (loaded version may be different from 8.2)";
                    failureReason = ourNeuterFailureReason;
                    return ourNeuterSucceeded = false;
                }
                if (field.FieldType != typeof(string[]))
                {
                    ourNeuterFailureReason = "implicitReferences field has unexpected type " + field.FieldType.FullName + " (expected string[])";
                    failureReason = ourNeuterFailureReason;
                    return ourNeuterSucceeded = false;
                }

                // .NET Core 3.0+ blocks FieldInfo.SetValue on `static readonly` (initonly)
                // fields with FieldAccessException. Emit a dynamic method that uses the
                // `stsfld` opcode directly — the verifier skips initonly checks for
                // dynamic methods when skipVisibility is true.
                DynamicMethod method = new DynamicMethod(
                    "RiderIlSpy_NeuterImplicitReferences",
                    typeof(void),
                    new[] { typeof(string[]) },
                    typeof(IlSpyDecompiler),
                    skipVisibility: true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, field);
                il.Emit(OpCodes.Ret);
                Action<string[]> setter = (Action<string[]>)method.CreateDelegate(typeof(Action<string[]>));
                setter(Array.Empty<string>());

                ourNeuterSucceeded = true;
                failureReason = null;
                return true;
            }
            catch (System.Exception ex)
            {
                ourNeuterFailureReason = ex.GetType().FullName + ": " + ex.Message;
                failureReason = ourNeuterFailureReason;
                return ourNeuterSucceeded = false;
            }
        }
    }

    // Detects ILSpy bug in DecompilerTypeSystem.InitializeAsync: when the assembly's
    // TargetFramework attribute parses to a 2-component Version (e.g. ".NETCoreApp,Version=v9.0"
    // or ".NETStandard,Version=v2.0"), ILSpy calls `version.ToString(3)` to format implicit
    // references, which throws ArgumentException with paramName="fieldCount".
    //
    // Present in 8.2, 9.1, 10.0 — never been fixed upstream. CSharp and CSharpWithIL modes
    // both go through DecompilerTypeSystem. IL-only mode uses ReflectionDisassembler and
    // skips this entire codepath, so it always works.
    internal static bool IsTwoComponentTfmVersionBug(ArgumentException ex)
    {
        if (ex.ParamName != "fieldCount") return false;
        // Walk the exception's stack frames looking for the
        // ICSharpCode.Decompiler.TypeSystem.DecompilerTypeSystem origin.
        // Type-identity beats string-matching the formatted StackTrace because
        // it survives stack-frame omissions in Release builds and is not
        // affected by namespace renames in user-facing trace text.
        System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(ex, fNeedFileInfo: false).GetFrames();
        foreach (System.Diagnostics.StackFrame frame in frames)
        {
            Type? declaringType = frame.GetMethod()?.DeclaringType;
            if (declaringType != null && declaringType.FullName == "ICSharpCode.Decompiler.TypeSystem.DecompilerTypeSystem")
                return true;
        }
        return false;
    }

    private string FallBackToIl(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs, ArgumentException original, System.Exception? retryFailure)
    {
        try
        {
            string il = DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs);
            StringBuilder sb = new StringBuilder();
            sb.Append("// RiderIlSpy: C# decompile hit ICSharpCode.Decompiler's 2-component TFM\n");
            sb.Append("// bug (e.g. .NET 10's '.NETCoreApp,Version=v10.0') and the reflection\n");
            sb.Append("// workaround couldn't be applied. Falling back to IL disassembly.\n");
            if (ourNeuterFailureReason != null)
                sb.Append("// Neuter failure: ").Append(ourNeuterFailureReason).Append('\n');
            if (retryFailure != null)
                sb.Append("// Retry after neuter also threw: ").Append(retryFailure.GetType().FullName).Append(": ").Append(retryFailure.Message).Append('\n');
            sb.Append("//\n");
            sb.Append(il);
            return sb.ToString();
        }
        catch (System.Exception fallbackEx)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FormatDecompileFailure(typeFullName, original));
            if (retryFailure != null)
            {
                sb.Append("\n// CSharp retry after neutering implicit refs also failed:\n");
                sb.Append(FormatDecompileFailure(typeFullName, retryFailure));
            }
            sb.Append("\n// IL fallback also failed:\n");
            sb.Append(FormatDecompileFailure(typeFullName, fallbackEx));
            return sb.ToString();
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
        try
        {
            DecompilerSettings effective = settings ?? new DecompilerSettings();
            using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
            UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, effective, extraSearchDirs);
            CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, effective);
            return decompiler.DecompileModuleAndAssemblyAttributesToString();
        }
        catch (System.Exception ex)
        {
            return FormatDecompileFailure(assemblyPath, ex);
        }
    }

    /// <summary>
    /// Reads identity metadata from a PE/CLI assembly without loading it into the
    /// AppDomain. Returns null when the file is unreadable or not a managed assembly.
    /// Used by the diagnostic banner to mirror the JetBrains decompiler's
    /// `// Assembly: ... // MVID: ...` header.
    /// </summary>
    public AssemblyBannerMetadata? GetAssemblyBannerMetadata(string assemblyPath)
    {
        // Body lives in IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata
        // (SDK-free, unit-testable). Wrapper here just adds Warn-on-null logging so
        // banner failures stay diagnosable in the IDE without polluting the helper.
        AssemblyBannerMetadata? result = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(assemblyPath);
        if (result == null && File.Exists(assemblyPath))
            ourLogger.Warn("RiderIlSpy.GetAssemblyBannerMetadata returned null for " + assemblyPath);
        return result;
    }

    /// <summary>
    /// Decompiles an entire assembly to a buildable C# project tree under <paramref name="targetDirectory"/>.
    /// Wraps ILSpy's <see cref="WholeProjectDecompiler"/> with the same resolver / search-dir setup that
    /// per-type decompilation uses, so the output respects the user's IlSpySettings (language version,
    /// async/await reconstruction, primary-ctor toggle, extra search dirs, etc.).
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly to decompile.</param>
    /// <param name="targetDirectory">Directory to write the project tree into. Created if missing.
    /// Existing files inside may be overwritten by ILSpy without warning — caller should pick a fresh dir.</param>
    /// <param name="settings">Decompiler settings; usually built via BuildDecompilerSettings from
    /// IlSpyExternalSourcesProvider so the IDE's user-facing toggles are honored.</param>
    /// <param name="extraSearchDirs">Optional extra assembly search dirs (matches DecompileType's contract).</param>
    /// <param name="cancellationToken">Cancellation; ILSpy honors it between types.</param>
    /// <returns>Summary describing where the project was written and how many .cs files it contains.</returns>
    public DecompileAssemblyToProjectResult DecompileAssemblyToProject(
        string assemblyPath,
        string targetDirectory,
        DecompilerSettings settings,
        IReadOnlyList<string>? extraSearchDirs = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);
        // 4-arg ctor is the only one that accepts custom DecompilerSettings — the
        // single-arg ctor builds its own defaults and exposes Settings as get-only.
        // ICSharpCode.Decompiler 10.x added an IProjectFileWriter slot in position 3
        // but Rider 2026.1 ships 8.2.x at runtime, so we MUST use the 8.2-shape ctor
        // here. Bumping the package without verifying Rider's bundled version led
        // to a MissingMethodException; see the csproj comment for the full story.
        WholeProjectDecompiler projectDecompiler = new WholeProjectDecompiler(
            settings,
            resolver,
            assemblyReferenceClassifier: null,
            debugInfoProvider: null);
        projectDecompiler.DecompileProject(module, targetDirectory, cancellationToken);

        string? projectFilePath = Directory.EnumerateFiles(targetDirectory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        int csharpFileCount = Directory.EnumerateFiles(targetDirectory, "*.cs", SearchOption.AllDirectories).Count();
        return new DecompileAssemblyToProjectResult(targetDirectory, projectFilePath, csharpFileCount);
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

    private static string DisassembleMixed(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs, bool emitCrosslinkMarkers = true)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        using StringWriter sw = new StringWriter();
        PlainTextOutput output = new PlainTextOutput(sw);
        MixedMethodBodyDisassembler bodyDisassembler = new MixedMethodBodyDisassembler(output, CancellationToken.None, settings, resolver, emitCrosslinkMarkers)
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
