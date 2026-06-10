using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ICSharpCode.Decompiler.Metadata;

namespace RiderIlSpy.Search;

public sealed class ConstantQueryHandler
{
    public List<ConstantHit> Scan(IEnumerable<PEFile> assemblies, string input)
    {
        List<ConstantHit> hits = new List<ConstantHit>();
        foreach (PEFile pe in assemblies)
        {
            MetadataReader reader = pe.Metadata;
            AssemblyId asm = AssemblyId.From(pe.FileName);
            int rowCount = reader.GetTableRowCount(TableIndex.Constant);
            for (int i = 1; i <= rowCount; i++)
            {
                ConstantHandle handle = MetadataTokens.ConstantHandle(i);
                Constant constant = reader.GetConstant(handle);
                string value = DecodeConstant(reader, constant);
                if (value.Contains(input, StringComparison.Ordinal))
                {
                    int token = MetadataTokens.GetToken(constant.Parent);
                    hits.Add(new ConstantHit(asm, token, value, constant.TypeCode.ToString()));
                }
            }
        }
        return hits;
    }

    private static string DecodeConstant(MetadataReader r, Constant c)
    {
        BlobReader br = r.GetBlobReader(c.Value);
        return c.TypeCode switch
        {
            ConstantTypeCode.Int32 => br.ReadInt32().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Int64 => br.ReadInt64().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Boolean => br.ReadBoolean().ToString(),
            ConstantTypeCode.String => br.ReadUTF16(br.Length),
            ConstantTypeCode.Char => br.ReadChar().ToString(),
            ConstantTypeCode.Double => br.ReadDouble().ToString(CultureInfo.InvariantCulture),
            ConstantTypeCode.Single => br.ReadSingle().ToString(CultureInfo.InvariantCulture),
            _ => "<unsupported>"
        };
    }
}
