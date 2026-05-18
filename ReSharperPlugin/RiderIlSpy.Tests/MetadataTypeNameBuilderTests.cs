using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler.Metadata;
using Xunit;

namespace RiderIlSpy.Tests;

// Pins MetadataTypeNameBuilder.BuildTypeFullName's three interesting branches:
//   (a) top-level type in a normal namespace -> "Namespace.Name"
//   (b) nested type                          -> "Outer+Inner" (recurse + '+')
//   (c) global-namespace type                -> bare "Name" (no leading dot)
// All three were previously exercised only indirectly through the smoke test,
// which uses RiderIlSpy.Tests.IlSpyDecompilerSmokeTests and BugTwoRecordFixture
// — both are top-level + namespaced, so the nested and global-namespace
// branches were silently uncovered.
//
// We test through a real System.Reflection.Metadata.MetadataReader instead of
// mocking it: MetadataReader / TypeDefinition are ref structs that cannot be
// stubbed without spinning up real PE bytes anyway, so loading the test
// assembly itself is both simpler and a more faithful end-to-end check than
// any test-double would be.
public class MetadataTypeNameBuilderTests
{
    public class OuterFixture
    {
        public class InnerFixture
        {
            public class DeeplyNestedFixture
            {
            }
        }
    }

    private static string SelfAssemblyPath => typeof(MetadataTypeNameBuilderTests).Assembly.Location;

    [Fact]
    public void BuildTypeFullName_returns_namespace_dot_name_for_top_level_type()
    {
        using PEFile module = new PEFile(SelfAssemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
        MetadataReader metadata = module.Metadata;
        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(metadata, "RiderIlSpy.Tests.MetadataTypeNameBuilderTests");
        Assert.False(handle.IsNil);
        TypeDefinition def = metadata.GetTypeDefinition(handle);
        Assert.Equal("RiderIlSpy.Tests.MetadataTypeNameBuilderTests", MetadataTypeNameBuilder.BuildTypeFullName(metadata, def));
    }

    [Fact]
    public void BuildTypeFullName_joins_nested_types_with_plus()
    {
        using PEFile module = new PEFile(SelfAssemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
        MetadataReader metadata = module.Metadata;
        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(metadata, "RiderIlSpy.Tests.MetadataTypeNameBuilderTests+OuterFixture+InnerFixture");
        Assert.False(handle.IsNil);
        TypeDefinition def = metadata.GetTypeDefinition(handle);
        Assert.Equal("RiderIlSpy.Tests.MetadataTypeNameBuilderTests+OuterFixture+InnerFixture", MetadataTypeNameBuilder.BuildTypeFullName(metadata, def));
    }

    [Fact]
    public void BuildTypeFullName_recurses_through_arbitrary_nesting_depth()
    {
        using PEFile module = new PEFile(SelfAssemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
        MetadataReader metadata = module.Metadata;
        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(metadata, "RiderIlSpy.Tests.MetadataTypeNameBuilderTests+OuterFixture+InnerFixture+DeeplyNestedFixture");
        Assert.False(handle.IsNil);
        TypeDefinition def = metadata.GetTypeDefinition(handle);
        Assert.Equal("RiderIlSpy.Tests.MetadataTypeNameBuilderTests+OuterFixture+InnerFixture+DeeplyNestedFixture", MetadataTypeNameBuilder.BuildTypeFullName(metadata, def));
    }

    [Fact]
    public void BuildTypeFullName_returns_bare_name_for_global_namespace_type()
    {
        // GlobalNamespaceFixture is declared with no `namespace` line — the
        // global-namespace branch returns the bare type name (no leading dot,
        // no namespace prefix). This regression test guards against an
        // accidental `"" + "." + name` shape that would render the type as
        // ".GlobalNamespaceFixture" — a tiny drift that would silently break
        // both the IlSpy decompile path and SourceLink lookup for any global-
        // namespace type.
        using PEFile module = new PEFile(SelfAssemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
        MetadataReader metadata = module.Metadata;
        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(metadata, "GlobalNamespaceFixture");
        Assert.False(handle.IsNil);
        TypeDefinition def = metadata.GetTypeDefinition(handle);
        Assert.Equal("GlobalNamespaceFixture", MetadataTypeNameBuilder.BuildTypeFullName(metadata, def));
    }

    [Fact]
    public void FindTypeHandle_returns_default_when_type_not_present()
    {
        using PEFile module = new PEFile(SelfAssemblyPath, PEStreamOptions.PrefetchMetadata, MetadataReaderOptions.Default);
        MetadataReader metadata = module.Metadata;
        TypeDefinitionHandle handle = MetadataTypeNameBuilder.FindTypeHandle(metadata, "Definitely.Not.A.Real.Type");
        Assert.True(handle.IsNil);
    }
}
