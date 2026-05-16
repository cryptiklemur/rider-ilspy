using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

[ShellComponent]
public class IlSpyDecompiler
{
    private static readonly ILogger ourLogger = Logger.GetLogger<IlSpyDecompiler>();

    // FetchSourceLink and the shared HttpClient moved to IlSpySourceLinkGateway
    // — that file owns the SourceLink integration boundary, including the
    // long-lived HttpClient. IlSpyDecompiler now only does ICSharpCode.Decompiler
    // work: DecompileType, DecompileAssemblyInfo, DecompileAssemblyToProject.

    public DecompileResult DecompileType(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs = null, IlSpyOutputMode mode = IlSpyOutputMode.CSharp)
    {
        try
        {
            return DecompileResult.Ok(DecompileForMode(assemblyPath, typeFullName, settings, extraSearchDirs, mode));
        }
        catch (ArgumentException ex) when (mode != IlSpyOutputMode.IL && DecompilerTypeSystemPatch.IsTwoComponentTfmVersionBug(ex))
        {
            // Hit ILSpy's two-component TFM bug; the retry path applies the
            // reflection patch and re-runs the same mode. Extracted into its
            // own method so the outer try/catch stays flat — no nested
            // try-inside-catch on the happy DecompileType reader.
            return RetryAfterTfmFix(assemblyPath, typeFullName, settings, extraSearchDirs, mode, ex);
        }
        catch (Exception ex)
        {
            return DecompileResult.Fail(FormatDecompileFailure(typeFullName, ex), ex.GetType().Name + ": " + ex.Message);
        }
    }

    private DecompileResult RetryAfterTfmFix(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs, IlSpyOutputMode mode, ArgumentException original)
    {
        if (!DecompilerTypeSystemPatch.TryNeuter())
            return FallBackToIl(assemblyPath, typeFullName, settings, extraSearchDirs, original, null);
        try
        {
            return DecompileResult.Ok(DecompileForMode(assemblyPath, typeFullName, settings, extraSearchDirs, mode));
        }
        catch (Exception retryEx)
        {
            return FallBackToIl(assemblyPath, typeFullName, settings, extraSearchDirs, original, retryEx);
        }
    }

    private string DecompileForMode(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs, IlSpyOutputMode mode) =>
        mode switch
        {
            IlSpyOutputMode.IL => DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs),
            IlSpyOutputMode.CSharpWithIL => DisassembleMixed(assemblyPath, typeFullName, settings, extraSearchDirs),
            _ => DecompileToCSharp(assemblyPath, typeFullName, settings, extraSearchDirs),
        };

    // The ICSharpCode.Decompiler version-compat patch (NeuterImplicitReferences +
    // IsTwoComponentTfmVersionBug + the static state + DynamicMethod IL emission)
    // moved to DecompilerTypeSystemPatch.cs so this file stays focused on the
    // decompile pipeline. Callers go through DecompilerTypeSystemPatch.{TryNeuter,
    // GetFailureReason, IsTwoComponentTfmVersionBug}.

    // Tiny shared comment-formatting helper. Both failure paths below build C#
    // comment blocks that Rider renders as decompiled "source"; centralizing the
    // `// ` prefix and divider conventions here keeps the layout consistent and
    // removes the hand-rolled StringBuilder pattern from each call site.
    private static class CommentBlock
    {
        public static StringBuilder Line(StringBuilder sb, string text) => sb.Append("// ").Append(text).Append('\n');
        public static StringBuilder Divider(StringBuilder sb) => sb.Append("//\n");
        public static StringBuilder IndentedLine(StringBuilder sb, int depth, string text)
        {
            sb.Append("// ");
            for (int i = 0; i < depth; i++) sb.Append("  ");
            return sb.Append(text).Append('\n');
        }
    }

    private DecompileResult FallBackToIl(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs, ArgumentException original, System.Exception? retryFailure)
    {
        try
        {
            string il = DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs);
            StringBuilder sb = new StringBuilder();
            CommentBlock.Line(sb, "RiderIlSpy: C# decompile hit ICSharpCode.Decompiler's 2-component TFM");
            CommentBlock.Line(sb, "bug (e.g. .NET 10's '.NETCoreApp,Version=v10.0') and the reflection");
            CommentBlock.Line(sb, "workaround couldn't be applied. Falling back to IL disassembly.");
            // DecompilerTypeSystemPatch.GetFailureReason snapshots the reason
            // under the same lock as the writers — guarantees consistency with
            // the success flag and avoids torn reads if more fields are added
            // to the failure-reason channel later.
            string? neuterFailure = DecompilerTypeSystemPatch.GetFailureReason();
            if (neuterFailure != null)
                CommentBlock.Line(sb, "Neuter failure: " + neuterFailure);
            if (retryFailure != null)
                CommentBlock.Line(sb, "Retry after neuter also threw: " + retryFailure.GetType().FullName + ": " + retryFailure.Message);
            CommentBlock.Divider(sb);
            sb.Append(il);
            // IL bytes ARE real source — even though we got here via the C# fallback
            // path, the user has usable disassembly. Marking this Ok lets the caller
            // cache it; Fail would be reserved for "all paths produced nothing but
            // a comment block".
            return DecompileResult.Ok(sb.ToString());
        }
        catch (Exception fallbackEx)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(FormatDecompileFailure(typeFullName, original));
            if (retryFailure != null)
            {
                sb.Append('\n');
                CommentBlock.Line(sb, "CSharp retry after neutering implicit refs also failed:");
                sb.Append(FormatDecompileFailure(typeFullName, retryFailure));
            }
            sb.Append('\n');
            CommentBlock.Line(sb, "IL fallback also failed:");
            sb.Append(FormatDecompileFailure(typeFullName, fallbackEx));
            return DecompileResult.Fail(sb.ToString(), "IL fallback failed: " + fallbackEx.GetType().Name + ": " + fallbackEx.Message);
        }
    }

    // Wraps an exception thrown out of ICSharpCode.Decompiler into a comment-only C# file
    // that Rider can display. Includes the full exception chain + stack trace so the user
    // can copy/paste it into a bug report without us needing a second round-trip.
    private static string FormatDecompileFailure(string typeFullName, System.Exception ex)
    {
        StringBuilder sb = new StringBuilder();
        CommentBlock.Line(sb, "RiderIlSpy decompile failed for " + typeFullName);
        CommentBlock.Divider(sb);
        CommentBlock.Line(sb, "This is almost always an ICSharpCode.Decompiler bug. Please file an issue at");
        CommentBlock.Line(sb, "https://github.com/cryptiklemur/rider-ilspy/issues with the type name and the");
        CommentBlock.Line(sb, "trace below.");
        CommentBlock.Divider(sb);
        System.Exception? current = ex;
        int depth = 0;
        while (current != null)
        {
            CommentBlock.IndentedLine(sb, depth, current.GetType().FullName + ": " + current.Message);
            if (!string.IsNullOrEmpty(current.StackTrace))
                foreach (string line in current.StackTrace.Split('\n'))
                    CommentBlock.IndentedLine(sb, depth + 1, line.TrimEnd('\r'));
            current = current.InnerException;
            depth++;
        }
        return sb.ToString();
    }
    public DecompileResult DecompileAssemblyInfo(string assemblyPath, DecompilerSettings? settings = null, IReadOnlyList<string>? extraSearchDirs = null)
    {
        try
        {
            DecompilerSettings effective = settings ?? new DecompilerSettings();
            using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
            UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, effective, extraSearchDirs);
            CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, effective);
            return DecompileResult.Ok(decompiler.DecompileModuleAndAssemblyAttributesToString());
        }
        catch (Exception ex)
        {
            return DecompileResult.Fail(FormatDecompileFailure(assemblyPath, ex), ex.GetType().Name + ": " + ex.Message);
        }
    }

    // GetAssemblyBannerMetadata removed — it was a one-liner that wrapped
    // IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata only to add
    // Warn-on-null logging, which belongs on the integration boundary rather
    // than mixed into ICSharpCode.Decompiler work. Provider callers now invoke
    // ReadAssemblyBannerMetadata directly and own their own logging.

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

    private static string DisassembleMixed(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(module.Metadata, typeFullName);
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

        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(module.Metadata, typeFullName);
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

    // FindTypeHandle was previously defined locally here; moved to
    // MetadataTypeNameBuilder.FindTypeHandle so the SourceLink PDB lookup
    // (PdbSourceLinkReader) and the decompile path use the exact same projection.
}
