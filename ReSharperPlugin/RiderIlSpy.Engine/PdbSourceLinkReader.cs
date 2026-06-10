using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace RiderIlSpy;

/// <summary>
/// Reads a portable PDB (embedded in the assembly's debug directory or a sidecar
/// .pdb next to it) and surfaces two pieces of information needed for the
/// SourceLink fallback:
/// 1. The SourceLink JSON blob, if any (parsed by <see cref="SourceLinkMapping"/>).
/// 2. Per-type document path lookup — given a CLR-reflection-style full type
///    name, returns the source file path that PDB sequence points reference for
///    that type, or <c>null</c> if the type spans multiple documents (partial
///    classes) or has no debug info (compiler-generated, abstract, etc.).
/// </summary>
/// <remarks>
/// Implementation note: portable PDBs are themselves ECMA-335 metadata blobs.
/// The <see cref="MethodDebugInformation"/> row tied to each method definition
/// names the source file directly via its <c>Document</c> column (single-file
/// shortcut) or per-sequence-point. We use the shortcut where present and
/// short-circuit when sequence points disagree — picking an arbitrary document
/// for a partial class would surface only one of its files, which is more
/// confusing than just falling back to decompilation.
/// </remarks>
public sealed class PdbSourceLinkReader : IDisposable
{
    private static readonly Guid ourSourceLinkKind = new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a");

    private readonly FileStream myAssemblyStream;
    private readonly PEReader myPeReader;
    private readonly MetadataReaderProvider? myPdbProvider;
    private readonly MetadataReader? myPdbReader;
    private readonly MetadataReader myPeMetadata;

    private PdbSourceLinkReader(FileStream assemblyStream, PEReader peReader, MetadataReader peMetadata, MetadataReaderProvider? pdbProvider, MetadataReader? pdbReader)
    {
        myAssemblyStream = assemblyStream;
        myPeReader = peReader;
        myPdbProvider = pdbProvider;
        myPdbReader = pdbReader;
        myPeMetadata = peMetadata;
    }

    /// <summary>
    /// Opens <paramref name="assemblyPath"/> and tries to load a portable PDB
    /// (embedded first, then sidecar at <c>Path.ChangeExtension(asm, ".pdb")</c>).
    /// Returns <c>null</c> if the PDB cannot be located or is not portable — in
    /// that case the SourceLink fallback is unavailable for this assembly.
    /// </summary>
    public static PdbSourceLinkReader? TryOpen(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return null;
        // Unified ownership trail — track every IDisposable we allocate so the
        // single finally can dispose what we created if any step fails. Earlier
        // versions hand-unwound each catch (asmStream → peReader → pdbProvider
        // re-disposed in three places), which was easy to skew when adding a
        // step. The transferOwnership flag flips to true only after the
        // PdbSourceLinkReader instance takes ownership of the chain.
        FileStream? asmStream = null;
        PEReader? peReader = null;
        MetadataReaderProvider? pdbProvider = null;
        bool transferOwnership = false;
        try
        {
            asmStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            peReader = new PEReader(asmStream, PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen);
            // GetMetadataReader is the canonical "is this actually a managed PE"
            // check — header-only failures throw here, not in the ctor. Calling
            // it eagerly means TryOpen never returns a reader that later
            // implodes mid-use.
            MetadataReader peMetadata = peReader.GetMetadataReader();

            // TryOpenEmbedded already swallows BadImageFormatException and
            // returns null, so no outer catch is needed here.
            pdbProvider = TryOpenEmbedded(peReader) ?? TryOpenSidecar(assemblyPath);
            if (pdbProvider == null) return null;

            MetadataReader pdbReader = pdbProvider.GetMetadataReader();
            PdbSourceLinkReader instance = new PdbSourceLinkReader(asmStream, peReader, peMetadata, pdbProvider, pdbReader);
            transferOwnership = true;
            return instance;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        // PEReader throws InvalidOperationException for "Image is too small"
        // before producing a usable MetadataReader.
        catch (InvalidOperationException) { return null; }
        catch (BadImageFormatException) { return null; }
        finally
        {
            if (!transferOwnership)
            {
                pdbProvider?.Dispose();
                peReader?.Dispose();
                asmStream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Extracts the raw SourceLink JSON blob attached to the module, or
    /// <c>null</c> when no SourceLink CustomDebugInformation entry exists.
    /// </summary>
    public string? TryReadSourceLinkJson()
    {
        if (myPdbReader == null) return null;
        foreach (CustomDebugInformationHandle handle in myPdbReader.CustomDebugInformation)
        {
            CustomDebugInformation cdi = myPdbReader.GetCustomDebugInformation(handle);
            Guid kind = myPdbReader.GetGuid(cdi.Kind);
            if (kind != ourSourceLinkKind) continue;
            BlobReader blob = myPdbReader.GetBlobReader(cdi.Value);
            return blob.ReadUTF8(blob.RemainingBytes);
        }
        return null;
    }

    /// <summary>
    /// Resolves <paramref name="typeFullName"/> (CLR-reflection format —
    /// <c>Namespace.Outer+Inner</c>) to the single source document path used
    /// across all of its methods, or <c>null</c> if no methods carry debug
    /// info or the type spans multiple documents.
    /// </summary>
    public string? TryGetPrimaryDocumentPath(string typeFullName)
    {
        if (myPdbReader == null) return null;
        TypeDefinitionHandle typeHandle = MetadataTypeNameBuilder.FindTypeHandle(myPeMetadata, typeFullName);
        if (typeHandle.IsNil) return null;

        TypeDefinition typeDef = myPeMetadata.GetTypeDefinition(typeHandle);
        string? candidate = null;
        foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods())
        {
            MethodDebugInformationHandle dbgHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));
            MethodDebugInformation dbg;
            try
            {
                dbg = myPdbReader.GetMethodDebugInformation(dbgHandle);
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            DocumentHandle docHandle = dbg.Document;
            if (docHandle.IsNil)
            {
                // dbg.Document being nil means EITHER (a) the method has no
                // sequence points at all (compiler-generated, abstract,
                // <Module>.cctor with stripped debug rows) OR (b) sequence points
                // span multiple documents (partial-class method body split across
                // files). Case (a) must skip — otherwise one stripped method
                // poisons the lookup for a type that's otherwise single-document.
                // Case (b) must give up — surfacing one file of a partial class
                // is more confusing than falling back to decompilation.
                if (dbg.SequencePointsBlob.IsNil) continue;
                return null;
            }
            string path = ReadDocumentPath(docHandle);
            if (string.IsNullOrEmpty(path)) continue;
            if (candidate == null)
            {
                candidate = path;
            }
            else if (!string.Equals(candidate, path, StringComparison.Ordinal))
            {
                return null;
            }
        }
        return candidate;
    }

    private string ReadDocumentPath(DocumentHandle handle)
    {
        if (myPdbReader == null) return string.Empty;
        Document doc = myPdbReader.GetDocument(handle);
        return myPdbReader.GetString(doc.Name);
    }



    private static MetadataReaderProvider? TryOpenEmbedded(PEReader peReader)
    {
        DebugDirectoryEntry? embedded = null;
        foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                embedded = entry;
                break;
            }
        }
        if (!embedded.HasValue) return null;
        try
        {
            return peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embedded.Value);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static MetadataReaderProvider? TryOpenSidecar(string assemblyPath)
    {
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath)) return null;
        FileStream stream;
        try
        {
            stream = new FileStream(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException)
        {
            return null;
        }
        try
        {
            return MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.PrefetchMetadata);
        }
        catch (BadImageFormatException)
        {
            stream.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        myPdbProvider?.Dispose();
        myPeReader.Dispose();
        // PEReader was constructed with LeaveOpen — we own the asm FileStream
        // and must dispose it here, else every successful TryOpen leaks a handle
        // (Windows: locks the assembly; Linux: just an fd leak, but still real).
        myAssemblyStream.Dispose();
    }
}
