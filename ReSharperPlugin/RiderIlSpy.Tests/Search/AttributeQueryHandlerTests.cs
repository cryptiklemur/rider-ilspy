using System.Collections.Generic;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class AttributeQueryHandlerTests
{
    private static IlSpySearchIndex Populate()
    {
        IlSpySearchIndex idx = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        idx.AddAttribute(new AttributeIndexEntry(asm, "System.ObsoleteAttribute", "ObsoleteAttribute", 1, "Type", "()"));
        idx.AddAttribute(new AttributeIndexEntry(asm, "System.SerializableAttribute", "SerializableAttribute", 2, "Type", "()"));
        return idx;
    }

    [Fact]
    public void By_Full_Name()
    {
        Assert.Single(new AttributeQueryHandler(Populate()).Query("System.ObsoleteAttribute"));
    }

    [Fact]
    public void By_Short_With_Suffix()
    {
        Assert.Single(new AttributeQueryHandler(Populate()).Query("ObsoleteAttribute"));
    }

    [Fact]
    public void By_Short_Without_Suffix()
    {
        Assert.Single(new AttributeQueryHandler(Populate()).Query("Obsolete"));
    }

    [Fact]
    public void Unknown_Returns_Empty()
    {
        Assert.Empty(new AttributeQueryHandler(Populate()).Query("Foo"));
    }
}
