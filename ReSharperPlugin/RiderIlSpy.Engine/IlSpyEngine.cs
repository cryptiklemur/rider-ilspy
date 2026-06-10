using System;
using System.Collections.Generic;
using System.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;

namespace RiderIlSpy;

/// <summary>
/// The engine's single entry point, instantiated by the host inside the
/// isolated AssemblyLoadContext via <see cref="IIlSpyEngine"/>. Converts the
/// SDK-free Contracts DTOs into real ICSharpCode.Decompiler types at this
/// boundary so no foreign type ever crosses back into the host context.
/// </summary>
public sealed class IlSpyEngine : IIlSpyEngine
{
    private readonly IlSpyDecompiler myDecompiler = new IlSpyDecompiler();
    private readonly IlSpyNavResolver myNavResolver = new IlSpyNavResolver();

    public string GetDecompilerVersion() => AssemblyBannerReader.GetDecompilerVersion();

    public DecompileResult DecompileType(
        string assemblyPath,
        string typeFullName,
        IlSpyDecompilerOptions options,
        IReadOnlyList<string>? extraSearchDirs = null,
        IlSpyOutputMode mode = IlSpyOutputMode.CSharp)
        => myDecompiler.DecompileType(assemblyPath, typeFullName, ToDecompilerSettings(options), extraSearchDirs, mode);

    public DecompileResult DecompileAssemblyInfo(
        string assemblyPath,
        IlSpyDecompilerOptions? options = null,
        IReadOnlyList<string>? extraSearchDirs = null)
        => myDecompiler.DecompileAssemblyInfo(assemblyPath, options == null ? null : ToDecompilerSettings(options), extraSearchDirs);

    public DecompileAssemblyToProjectResult DecompileAssemblyToProject(
        string assemblyPath,
        string targetDirectory,
        IlSpyDecompilerOptions options,
        IReadOnlyList<string>? extraSearchDirs = null,
        CancellationToken cancellationToken = default)
        => myDecompiler.DecompileAssemblyToProject(assemblyPath, targetDirectory, ToDecompilerSettings(options), extraSearchDirs, cancellationToken);

    public AssemblyBannerMetadata? ReadAssemblyBannerMetadata(string assemblyPath)
        => AssemblyBannerReader.ReadAssemblyBannerMetadata(assemblyPath);

    public PdbSourceLinkInfo? ReadPdbSourceLinkInfo(string assemblyPath, string typeFullName)
    {
        using PdbSourceLinkReader? pdb = PdbSourceLinkReader.TryOpen(assemblyPath);
        if (pdb == null) return null;
        return new PdbSourceLinkInfo(pdb.TryReadSourceLinkJson(), pdb.TryGetPrimaryDocumentPath(typeFullName));
    }

    public IlSpyNavResolution ResolveNavigation(string assemblyPath, int metadataToken, int ilOffset)
        => myNavResolver.Resolve(assemblyPath, metadataToken, ilOffset);

    public IlSpySearchIndex BuildSearchIndex(
        IEnumerable<string> assemblyPaths,
        Action<IlSpyIndexBuildProgress> onProgress,
        CancellationToken cancellationToken,
        Action<string, Exception>? onSkipped = null)
        => new IlSpySearchIndexer().BuildAll(assemblyPaths, onProgress, cancellationToken, onSkipped);

    public void IndexAssembly(string assemblyPath, IlSpySearchIndex index)
    {
        using PEFile pe = new PEFile(assemblyPath);
        AssemblyMetadata metadata = AssemblyMetadata.From(assemblyPath);
        IlSpySearchIndexer indexer = new IlSpySearchIndexer();
        indexer.IndexLiterals(pe, metadata, index);
        indexer.IndexAttributes(pe, metadata, index);
        indexer.IndexResources(pe, metadata, index);
    }

    public List<ConstantHit> ScanConstants(IReadOnlyList<string> assemblyPaths, string input)
    {
        List<PEFile> peFiles = new List<PEFile>(assemblyPaths.Count);
        try
        {
            foreach (string path in assemblyPaths)
            {
                try { peFiles.Add(new PEFile(path)); }
                catch { /* skip unreadable assemblies */ }
            }
            return new ConstantQueryHandler().Scan(peFiles, input);
        }
        finally
        {
            foreach (PEFile pe in peFiles) pe.Dispose();
        }
    }

    // Mirrors what IlSpyRequestSettingsBuilder used to do host-side before the
    // load-context split: language-version downgrade applies after construction
    // so Latest leaves ILSpy's defaults untouched (SetLanguageVersion flips
    // multiple feature flags — RecordClasses, InitAccessors, ... — to match the
    // target version's capability set).
    private static DecompilerSettings ToDecompilerSettings(IlSpyDecompilerOptions options)
    {
        DecompilerSettings settings = new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = options.ThrowOnAssemblyResolveErrors,
            AsyncAwait = options.AsyncAwait,
            UseExpressionBodyForCalculatedGetterOnlyProperties = options.ExpressionBodies,
            NamedArguments = options.NamedArguments,
            ShowXmlDocumentation = options.ShowXmlDocumentation,
            RemoveDeadCode = options.RemoveDeadCode,
            UsePrimaryConstructorSyntax = options.UsePrimaryConstructorSyntax,
        };
        if (options.LanguageVersion != IlSpyLanguageVersion.Latest)
            settings.SetLanguageVersion((LanguageVersion)(int)options.LanguageVersion);
        return settings;
    }
}
