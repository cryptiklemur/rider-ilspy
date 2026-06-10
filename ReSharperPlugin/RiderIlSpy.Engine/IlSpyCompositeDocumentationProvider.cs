using System.Collections.Generic;
using System.IO;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;

namespace RiderIlSpy;

/// <summary>
/// <see cref="IDocumentationProvider"/> that fans out to a list of inner
/// providers and returns the first non-null hit. Used so we can compose
/// multiple xmldoc files for a single decompile pass — e.g. the impl-side
/// sidecar plus every <c>*.xml</c> file in the parallel .NET ref pack —
/// without forcing the caller to pick a single provider up front.
///
/// The order of <paramref name="inner"/> at construction is the lookup
/// order. Callers should put the most authoritative source first
/// (typically the sidecar next to the impl assembly), then enumerate
/// ref-pack files; an IEntity that happens to be documented in multiple
/// files will return the first one. Empty results from
/// <see cref="IDocumentationProvider.GetDocumentation(IEntity)"/> are
/// treated as "no documentation" so the next provider gets a turn —
/// XmlDocumentationProvider returns null on miss, but defensive null-or-
/// empty handling guards against well-meaning providers returning "" for
/// "I don't know."
/// </summary>
public sealed class IlSpyCompositeDocumentationProvider : IDocumentationProvider
{
    private readonly IReadOnlyList<IDocumentationProvider> myInner;

    public IlSpyCompositeDocumentationProvider(IReadOnlyList<IDocumentationProvider> inner)
    {
        myInner = inner;
    }

    public string? GetDocumentation(IEntity entity)
    {
        foreach (IDocumentationProvider provider in myInner)
        {
            string? doc = provider.GetDocumentation(entity);
            if (!string.IsNullOrEmpty(doc)) return doc;
        }
        return null;
    }

    /// <summary>
    /// I/O-bound factory: resolves and loads every xmldoc source we can find
    /// for <paramref name="assemblyPath"/>, returning a composite provider
    /// over them — or <c>null</c> when nothing on disk matches. Order is
    /// sidecar first (most authoritative for user packages / Mono / .NETFx
    /// ref assemblies), then every <c>*.xml</c> under the .NET ref-pack
    /// redirect target (covers BCL types whose xmldocs live in some
    /// type-forwarder-target assembly's xml). Individual file load failures
    /// (malformed XML, locked file) are swallowed so a single bad file in a
    /// ~100-xml ref pack doesn't blow up the whole decompile.
    /// </summary>
    public static IDocumentationProvider? BuildForAssembly(string? assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath)) return null;
        List<IDocumentationProvider> providers = new List<IDocumentationProvider>();

        string? sidecar = IlSpyXmlDocResolver.GetSidecarXmlDocPath(assemblyPath);
        if (sidecar != null && File.Exists(sidecar))
        {
            try { providers.Add(new XmlDocumentationProvider(sidecar)); }
            catch { /* malformed xml is non-fatal; skip and try the next source */ }
        }

        string? refPackDir = IlSpyXmlDocResolver.GetRefPackXmlDocDirectory(assemblyPath);
        if (refPackDir != null && Directory.Exists(refPackDir))
        {
            string[] xmlFiles;
            try { xmlFiles = Directory.GetFiles(refPackDir, "*.xml", SearchOption.AllDirectories); }
            catch { xmlFiles = System.Array.Empty<string>(); }
            foreach (string xmlPath in xmlFiles)
            {
                // Avoid double-loading when the sidecar happens to live
                // under the ref-pack directory (shouldn't happen given how
                // the resolver maps paths, but cheap to guard against).
                if (sidecar != null && string.Equals(xmlPath, sidecar, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                try { providers.Add(new XmlDocumentationProvider(xmlPath)); }
                catch { /* individual xml load failure is non-fatal */ }
            }
        }

        return providers.Count == 0 ? null : new IlSpyCompositeDocumentationProvider(providers);
    }
}
