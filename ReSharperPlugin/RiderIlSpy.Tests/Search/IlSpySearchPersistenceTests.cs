using System;
using System.IO;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchPersistenceTests
{
    [Fact]
    public void Round_Trip_Literals()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            IlSpySearchIndex written = new IlSpySearchIndex();
            AssemblyId asm = AssemblyId.From("/x/a.dll");
            written.RegisterAssembly(new AssemblyMetadata(asm, "/x/a.dll", DateTime.UtcNow, 1024));
            written.AddLiteral(new LiteralIndexEntry(asm, 1, 0x06_000_001, 0, "hello"));
            new IlSpySearchPersistence().Save(written, tmp);

            IlSpySearchIndex? read = new IlSpySearchPersistence().Load(tmp);
            Assert.NotNull(read);
            System.Collections.Generic.List<LiteralIndexEntry> hits = read!.LookupLiteralCandidatesByTrigram("hel", false);
            Assert.Single(hits);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Round_Trip_Attributes()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            IlSpySearchIndex written = new IlSpySearchIndex();
            AssemblyId asm = AssemblyId.From("/x/b.dll");
            written.RegisterAssembly(new AssemblyMetadata(asm, "/x/b.dll", DateTime.UtcNow, 512));
            written.AddAttribute(new AttributeIndexEntry(asm, "System.ObsoleteAttribute", "ObsoleteAttribute", 0x02_000_001, "Type", "(...)"));
            new IlSpySearchPersistence().Save(written, tmp);

            IlSpySearchIndex? read = new IlSpySearchPersistence().Load(tmp);
            Assert.NotNull(read);
            System.Collections.Generic.List<AttributeIndexEntry> hits = read!.LookupAttributesByFqn("System.ObsoleteAttribute");
            Assert.Single(hits);
            Assert.Equal("ObsoleteAttribute", hits[0].AttributeTypeShortName);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Round_Trip_Resources()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            IlSpySearchIndex written = new IlSpySearchIndex();
            AssemblyId asm = AssemblyId.From("/x/c.dll");
            written.RegisterAssembly(new AssemblyMetadata(asm, "/x/c.dll", DateTime.UtcNow, 256));
            written.AddResource(new ResourceIndexEntry(asm, 0x28_000_001, "MyApp.Properties.Resources.resources", null, 4096, "resources"));
            written.AddResource(new ResourceIndexEntry(asm, 0x28_000_001, "greeting", "MyApp.Properties.Resources.resources", 0, "text"));
            new IlSpySearchPersistence().Save(written, tmp);

            IlSpySearchIndex? read = new IlSpySearchPersistence().Load(tmp);
            Assert.NotNull(read);
            System.Collections.Generic.List<ResourceIndexEntry> hits = read!.LookupResourceCandidatesByTrigram("gre");
            Assert.NotEmpty(hits);
            Assert.Equal("MyApp.Properties.Resources.resources", hits[0].ParentEntryName);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Load_Returns_Null_On_Version_Mismatch()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            // Write bad magic (8 bytes so EndOfStreamException is not hit before magic check)
            File.WriteAllBytes(tmp, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x00 });
            Assert.Null(new IlSpySearchPersistence().Load(tmp));
        }
        finally
        {
            // File may have been deleted by Load's catch block; ignore if gone
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
