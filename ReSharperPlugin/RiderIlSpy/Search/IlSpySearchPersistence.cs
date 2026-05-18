using System;
using System.Collections.Generic;
using System.IO;

namespace RiderIlSpy.Search;

public sealed class IlSpySearchPersistence
{
    private const int FormatMagic = 0x49_4C_53_50; // "ILSP"
    private const int FormatVersion = 2; // v2: NormalizedPath preserves original case (was lowercased in v1)

    public void Save(IlSpySearchIndex index, string path)
    {
        using FileStream fs = File.Create(path);
        using BinaryWriter bw = new BinaryWriter(fs);
        bw.Write(FormatMagic);
        bw.Write(FormatVersion);

        IReadOnlyCollection<AssemblyMetadata> assemblies = index.RegisteredAssemblies();
        bw.Write(assemblies.Count);
        foreach (AssemblyMetadata asm in assemblies)
        {
            bw.Write(asm.Id.NormalizedPath);
            bw.Write(asm.DisplayPath);
            bw.Write(asm.LastWriteTimeUtc.Ticks);
            bw.Write(asm.FileSize);
        }

        List<LiteralIndexEntry> literals = new List<LiteralIndexEntry>(index.AllLiteralEntries());
        bw.Write(literals.Count);
        foreach (LiteralIndexEntry e in literals)
        {
            bw.Write(e.AssemblyId.NormalizedPath);
            bw.Write(e.UserStringToken);
            bw.Write(e.ContainingMethodToken);
            bw.Write(e.IlOffset);
            bw.Write(e.StringValue);
        }

        List<AttributeIndexEntry> attributes = new List<AttributeIndexEntry>(index.AllAttributeEntries());
        bw.Write(attributes.Count);
        foreach (AttributeIndexEntry e in attributes)
        {
            bw.Write(e.AssemblyId.NormalizedPath);
            bw.Write(e.AttributeTypeFullName);
            bw.Write(e.AttributeTypeShortName);
            bw.Write(e.TargetMetadataToken);
            bw.Write(e.TargetKind);
            bw.Write(e.ArgsSummary);
        }

        List<ResourceIndexEntry> resources = new List<ResourceIndexEntry>(index.AllResourceEntries());
        bw.Write(resources.Count);
        foreach (ResourceIndexEntry e in resources)
        {
            bw.Write(e.AssemblyId.NormalizedPath);
            bw.Write(e.ManifestResourceToken);
            bw.Write(e.ResourceName);
            bw.Write(e.ParentEntryName ?? "");
            bw.Write(e.SizeBytes);
            bw.Write(e.MimeHint);
        }
    }

    public IlSpySearchIndex? Load(string path)
    {
        try
        {
            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new BinaryReader(fs);
            if (br.ReadInt32() != FormatMagic) return null;
            if (br.ReadInt32() != FormatVersion) return null;

            IlSpySearchIndex index = new IlSpySearchIndex();

            int asmCount = br.ReadInt32();
            for (int i = 0; i < asmCount; i++)
            {
                AssemblyId id = new AssemblyId(br.ReadString());
                string display = br.ReadString();
                long ticks = br.ReadInt64();
                long size = br.ReadInt64();
                index.RegisterAssembly(new AssemblyMetadata(id, display, new DateTime(ticks, DateTimeKind.Utc), size));
            }

            int litCount = br.ReadInt32();
            for (int i = 0; i < litCount; i++)
            {
                AssemblyId id = new AssemblyId(br.ReadString());
                int usToken = br.ReadInt32();
                int methodTok = br.ReadInt32();
                int ilOffset = br.ReadInt32();
                string val = br.ReadString();
                index.AddLiteral(new LiteralIndexEntry(id, usToken, methodTok, ilOffset, val));
            }

            int attrCount = br.ReadInt32();
            for (int i = 0; i < attrCount; i++)
            {
                AssemblyId id = new AssemblyId(br.ReadString());
                string fullName = br.ReadString();
                string shortName = br.ReadString();
                int targetToken = br.ReadInt32();
                string targetKind = br.ReadString();
                string argsSummary = br.ReadString();
                index.AddAttribute(new AttributeIndexEntry(id, fullName, shortName, targetToken, targetKind, argsSummary));
            }

            int resCount = br.ReadInt32();
            for (int i = 0; i < resCount; i++)
            {
                AssemblyId id = new AssemblyId(br.ReadString());
                int token = br.ReadInt32();
                string name = br.ReadString();
                string parentRaw = br.ReadString();
                string? parent = parentRaw.Length == 0 ? null : parentRaw;
                long size = br.ReadInt64();
                string mime = br.ReadString();
                index.AddResource(new ResourceIndexEntry(id, token, name, parent, size, mime));
            }

            return index;
        }
        catch
        {
            try { File.Delete(path); } catch { /* best-effort */ }
            return null;
        }
    }
}
