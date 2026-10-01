using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexerOperandTests
{
    [Theory]
    [InlineData(ILOpCode.Ldftn, 4)]
    [InlineData(ILOpCode.Ldvirtftn, 4)]
    [InlineData(ILOpCode.Unaligned, 1)]
    public void Opcodes_Absent_From_The_Old_Hand_Written_Table_Have_Operands(ILOpCode op, int expected)
    {
        Assert.Equal(expected, IlSpySearchIndexer.OperandSize(op));
    }

    [Theory]
    [InlineData(ILOpCode.Nop, 0)]
    [InlineData(ILOpCode.Ldc_i4_s, 1)]
    [InlineData(ILOpCode.Ldloc, 2)]
    [InlineData(ILOpCode.Call, 4)]
    [InlineData(ILOpCode.Ldc_r4, 4)]
    [InlineData(ILOpCode.Ldc_i8, 8)]
    [InlineData(ILOpCode.Ldc_r8, 8)]
    public void Known_Operand_Sizes_Are_Unchanged(ILOpCode op, int expected)
    {
        Assert.Equal(expected, IlSpySearchIndexer.OperandSize(op));
    }

    [Fact]
    public void Every_Opcode_Has_A_Defined_Operand_Size()
    {
        int[] allowed = [0, 1, 2, 4, 8];
        List<ILOpCode> bad = Enum.GetValues<ILOpCode>()
            .Where(op => op != ILOpCode.Switch && !allowed.Contains(IlSpySearchIndexer.OperandSize(op)))
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void Lambda_Bearing_Assembly_Indexes_Its_Literals()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "ldftn.dll");
        using PEFile pe = new PEFile(path);
        IlSpySearchIndex index = new IlSpySearchIndex();

        new IlSpySearchIndexer().IndexLiterals(pe, AssemblyMetadata.From(path), index);

        List<string> values = index.AllLiteralEntries().Select(e => e.StringValue).ToList();
        Assert.Contains("ldftn-needle", values);
        Assert.Contains("after-ldftn-needle", values);
    }
}
