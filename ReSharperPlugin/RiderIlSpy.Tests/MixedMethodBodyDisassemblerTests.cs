using System.Collections.Generic;
using ICSharpCode.Decompiler.DebugInfo;
using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Dedicated coverage for <see cref="MixedMethodBodyDisassembler"/>'s
/// binary-search helper. The class itself is bound to ICSharpCode.Decompiler
/// internals which require a PE-loaded method body to drive end-to-end, but the
/// offset lookup is pure list math and worth pinning against accidental
/// off-by-one or wrong-comparator regressions. The CSharpWithIL smoke test in
/// <c>IlSpyDecompilerSmokeTests</c> still covers the end-to-end emission path;
/// these facts cover the lookup contract in isolation.
/// </summary>
public sealed class MixedMethodBodyDisassemblerTests
{
    private static SequencePoint At(int offset) => new SequencePoint { Offset = offset };

    [Fact]
    public void BinarySearchByOffset_returns_exact_index_on_hit()
    {
        List<SequencePoint> sps = new List<SequencePoint> { At(0), At(10), At(25), At(40) };
        Assert.Equal(0, MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 0));
        Assert.Equal(1, MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 10));
        Assert.Equal(2, MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 25));
        Assert.Equal(3, MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 40));
    }

    [Fact]
    public void BinarySearchByOffset_returns_bitwise_complement_on_miss()
    {
        List<SequencePoint> sps = new List<SequencePoint> { At(0), At(10), At(25), At(40) };
        // Misses follow Array.BinarySearch convention: ~result == insertion index.
        Assert.True(MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 5) < 0);
        Assert.Equal(1, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 5));
        Assert.Equal(3, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 30));
        Assert.Equal(4, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 100));
        Assert.Equal(0, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, -5));
    }

    [Fact]
    public void BinarySearchByOffset_handles_empty_list()
    {
        Assert.Equal(0, ~MixedMethodBodyDisassembler.BinarySearchByOffset(new List<SequencePoint>(), 42));
    }

    [Fact]
    public void BinarySearchByOffset_handles_single_element()
    {
        List<SequencePoint> sps = new List<SequencePoint> { At(50) };
        Assert.Equal(0, MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 50));
        Assert.Equal(0, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 10));
        Assert.Equal(1, ~MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 60));
    }

    [Fact]
    public void BinarySearchByOffset_with_duplicate_offsets_returns_some_match()
    {
        List<SequencePoint> sps = new List<SequencePoint> { At(10), At(10), At(20) };
        int idx = MixedMethodBodyDisassembler.BinarySearchByOffset(sps, 10);
        // Binary search over duplicates may land on either index; the contract
        // is just "non-negative and points at a matching offset".
        Assert.True(idx >= 0);
        Assert.Equal(10, sps[idx].Offset);
    }
}
