using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using ICSharpCode.Decompiler.Metadata;

namespace RiderIlSpy.Search;

public sealed class IlSpySearchIndexer
{
    public void IndexLiterals(PEFile peFile, AssemblyMetadata metadata, IlSpySearchIndex target)
    {
        target.RegisterAssembly(metadata);
        MetadataReader reader = peFile.Metadata;

        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0) continue;
            MethodBodyBlock body;
            try { body = peFile.Reader.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }

            BlobReader il = body.GetILReader();
            while (il.Offset < il.Length)
            {
                int ilOffset = il.Offset;
                ILOpCode op = ReadOpCode(ref il);
                if (op == ILOpCode.Ldstr)
                {
                    int token = il.ReadInt32();
                    UserStringHandle ush = MetadataTokens.UserStringHandle(token);
                    string value = reader.GetUserString(ush);
                    target.AddLiteral(new LiteralIndexEntry(
                        metadata.Id,
                        token,
                        MetadataTokens.GetToken(methodHandle),
                        ilOffset,
                        value));
                }
                else
                {
                    SkipOperand(ref il, op);
                }
            }
        }
    }

    public void IndexAttributes(PEFile peFile, AssemblyMetadata metadata, IlSpySearchIndex target)
    {
        target.RegisterAssembly(metadata);
        MetadataReader reader = peFile.Metadata;

        foreach (CustomAttributeHandle handle in reader.CustomAttributes)
        {
            CustomAttribute attr = reader.GetCustomAttribute(handle);
            string fqn = ResolveAttributeTypeFqn(reader, attr);
            string shortName = fqn.Contains('.') ? fqn[(fqn.LastIndexOf('.') + 1)..] : fqn;
            EntityHandle parent = attr.Parent;
            string targetKind = ClassifyParent(parent);
            int targetToken = MetadataTokens.GetToken(parent);
            target.AddAttribute(new AttributeIndexEntry(
                metadata.Id, fqn, shortName, targetToken, targetKind, ArgsSummary(attr)));
        }
    }

    public void IndexResources(PEFile peFile, AssemblyMetadata metadata, IlSpySearchIndex target)
    {
        target.RegisterAssembly(metadata);
        MetadataReader reader = peFile.Metadata;

        foreach (ManifestResourceHandle handle in reader.ManifestResources)
        {
            ManifestResource mr = reader.GetManifestResource(handle);
            string name = reader.GetString(mr.Name);
            int token = MetadataTokens.GetToken(handle);
            long size = TryGetResourceSize(peFile, mr);
            string mime = SniffMime(name);
            target.AddResource(new ResourceIndexEntry(
                metadata.Id, token, name, ParentEntryName: null, size, mime));

            if (name.EndsWith(".resources", System.StringComparison.OrdinalIgnoreCase))
            {
                foreach (string innerKey in EnumerateResourcesEntries(peFile, mr))
                {
                    target.AddResource(new ResourceIndexEntry(
                        metadata.Id, token, innerKey, ParentEntryName: name, 0, "text"));
                }
            }
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader r)
    {
        byte b = r.ReadByte();
        if (b == 0xFE)
        {
            byte b2 = r.ReadByte();
            return (ILOpCode)(0xFE00 | b2);
        }
        return (ILOpCode)b;
    }

    private static void SkipOperand(ref BlobReader r, ILOpCode op)
    {
        switch (op)
        {
            case ILOpCode.Switch:
                int count = r.ReadInt32();
                r.Offset += count * 4;
                break;
            case ILOpCode.Br_s: case ILOpCode.Brfalse_s: case ILOpCode.Brtrue_s:
            case ILOpCode.Beq_s: case ILOpCode.Bge_s: case ILOpCode.Bgt_s:
            case ILOpCode.Ble_s: case ILOpCode.Blt_s: case ILOpCode.Bne_un_s:
            case ILOpCode.Bge_un_s: case ILOpCode.Bgt_un_s: case ILOpCode.Ble_un_s:
            case ILOpCode.Blt_un_s: case ILOpCode.Leave_s:
            case ILOpCode.Ldarg_s: case ILOpCode.Ldarga_s: case ILOpCode.Starg_s:
            case ILOpCode.Ldloc_s: case ILOpCode.Ldloca_s: case ILOpCode.Stloc_s:
            case ILOpCode.Ldc_i4_s:
                r.Offset += 1; break;
            case ILOpCode.Ldarg: case ILOpCode.Ldarga: case ILOpCode.Starg:
            case ILOpCode.Ldloc: case ILOpCode.Ldloca: case ILOpCode.Stloc:
                r.Offset += 2; break;
            case ILOpCode.Br: case ILOpCode.Brfalse: case ILOpCode.Brtrue:
            case ILOpCode.Beq: case ILOpCode.Bge: case ILOpCode.Bgt:
            case ILOpCode.Ble: case ILOpCode.Blt: case ILOpCode.Bne_un:
            case ILOpCode.Bge_un: case ILOpCode.Bgt_un: case ILOpCode.Ble_un:
            case ILOpCode.Blt_un: case ILOpCode.Leave: case ILOpCode.Ldc_i4:
            case ILOpCode.Call: case ILOpCode.Calli: case ILOpCode.Callvirt:
            case ILOpCode.Jmp: case ILOpCode.Newobj: case ILOpCode.Castclass:
            case ILOpCode.Isinst: case ILOpCode.Unbox: case ILOpCode.Unbox_any:
            case ILOpCode.Ldfld: case ILOpCode.Ldflda: case ILOpCode.Stfld:
            case ILOpCode.Ldsfld: case ILOpCode.Ldsflda: case ILOpCode.Stsfld:
            case ILOpCode.Box: case ILOpCode.Newarr: case ILOpCode.Ldelema:
            case ILOpCode.Ldelem: case ILOpCode.Stelem: case ILOpCode.Refanyval:
            case ILOpCode.Mkrefany: case ILOpCode.Ldtoken: case ILOpCode.Ldobj:
            case ILOpCode.Stobj: case ILOpCode.Cpobj: case ILOpCode.Initobj:
            case ILOpCode.Sizeof: case ILOpCode.Constrained:
                r.Offset += 4; break;
            case ILOpCode.Ldc_i8: case ILOpCode.Ldc_r8:
                r.Offset += 8; break;
            case ILOpCode.Ldc_r4:
                r.Offset += 4; break;
        }
    }

    private static string ResolveAttributeTypeFqn(MetadataReader reader, CustomAttribute attr)
    {
        EntityHandle ctorHandle = attr.Constructor;
        EntityHandle typeHandle = ctorHandle.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)ctorHandle).Parent,
            _ => default
        };
        if (typeHandle.IsNil) return "<unknown>";
        return typeHandle.Kind switch
        {
            HandleKind.TypeDefinition => FormatTypeDef(reader, (TypeDefinitionHandle)typeHandle),
            HandleKind.TypeReference => FormatTypeRef(reader, (TypeReferenceHandle)typeHandle),
            _ => "<unknown>"
        };
    }

    private static string FormatTypeDef(MetadataReader r, TypeDefinitionHandle h)
    {
        TypeDefinition td = r.GetTypeDefinition(h);
        string ns = r.GetString(td.Namespace);
        string name = r.GetString(td.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string FormatTypeRef(MetadataReader r, TypeReferenceHandle h)
    {
        TypeReference tr = r.GetTypeReference(h);
        string ns = r.GetString(tr.Namespace);
        string name = r.GetString(tr.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string ClassifyParent(EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeDefinition => "Type",
        HandleKind.MethodDefinition => "Method",
        HandleKind.FieldDefinition => "Field",
        HandleKind.PropertyDefinition => "Property",
        HandleKind.EventDefinition => "Event",
        HandleKind.Parameter => "Parameter",
        HandleKind.AssemblyDefinition => "Assembly",
        HandleKind.ModuleDefinition => "Module",
        _ => "Unknown"
    };

    private static string ArgsSummary(CustomAttribute attr)
    {
        int blobLen = attr.Value.IsNil ? 0 : 1;
        return blobLen == 0 ? "()" : "(...)";
    }

    private static long TryGetResourceSize(PEFile peFile, ManifestResource mr)
    {
        if (!mr.Implementation.IsNil) return 0;
        try
        {
            System.Reflection.PortableExecutable.DirectoryEntry? section =
                peFile.Reader.PEHeaders.CorHeader?.ResourcesDirectory;
            return section.HasValue ? section.Value.Size : 0;
        }
        catch { return 0; }
    }

    private static string SniffMime(string name)
    {
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" => "image",
            ".xml" or ".json" or ".html" or ".txt" or ".resx" => "text",
            ".resources" => "resources",
            _ => "binary"
        };
    }

    private static IEnumerable<string> EnumerateResourcesEntries(PEFile peFile, ManifestResource mr)
    {
        // v1 stub — inner .resources enumeration deferred to Polish phase
        yield break;
    }

    public IlSpySearchIndex BuildAll(
        IEnumerable<string> assemblyPaths,
        Action<IlSpyIndexBuildProgress> onProgress,
        CancellationToken ct,
        Action<string, Exception>? onSkipped = null)
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        List<string> paths = new List<string>(assemblyPaths);
        IlSpyIndexBuildProgress progress = new IlSpyIndexBuildProgress { Total = paths.Count };
        foreach (string path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using PEFile pe = new PEFile(path);
                AssemblyMetadata metadata = AssemblyMetadata.From(path);
                IndexLiterals(pe, metadata, index);
                IndexAttributes(pe, metadata, index);
                IndexResources(pe, metadata, index);
                progress.Indexed++;
            }
            catch (Exception ex)
            {
                progress.Skipped++;
                onSkipped?.Invoke(path, ex);
            }
            onProgress(progress);
        }
        return index;
    }
}
