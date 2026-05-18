using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class AssemblyIdTests
{
    [Fact]
    public void Equal_For_Same_Normalized_Path()
    {
        AssemblyId a = AssemblyId.From("/x/foo/bar.dll");
        AssemblyId b = AssemblyId.From("/x/foo/bar.dll");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_For_Different_Paths()
    {
        Assert.NotEqual(AssemblyId.From("/x/a.dll"), AssemblyId.From("/x/b.dll"));
    }

    [Fact]
    public void Preserves_Original_Case_In_NormalizedPath()
    {
        // Regression for navigation on case-sensitive filesystems (Linux): the
        // navigation handler calls File.Exists on AssemblyId.NormalizedPath, so
        // a previously-lowercased path would never match a real on-disk file
        // whose name has any uppercase letters.
        AssemblyId id = AssemblyId.From("/X/Foo/Bar.DLL");
        Assert.Contains("Bar.DLL", id.NormalizedPath);
        Assert.Contains("Foo", id.NormalizedPath);
    }

    [Fact]
    public void Equal_For_Same_Path_With_Different_Case()
    {
        // On case-insensitive filesystems (Windows, default macOS APFS), the
        // same on-disk file can be referenced with different case; dedup should
        // still treat them as the same indexed assembly.
        AssemblyId a = AssemblyId.From("/X/Foo/Bar.dll");
        AssemblyId b = AssemblyId.From("/x/foo/bar.DLL");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
