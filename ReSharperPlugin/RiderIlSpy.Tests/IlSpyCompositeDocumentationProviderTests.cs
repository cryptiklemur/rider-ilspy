using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;
using Xunit;

namespace RiderIlSpy.Tests;

// Pins the composite IDocumentationProvider behavior. Three rules:
//   1. First non-null inner result wins.
//   2. Empty strings are treated as "no documentation" so a well-meaning
//      provider returning "" doesn't shadow a real later hit.
//   3. All-miss returns null (callers / ILSpy treat null as "no xmldoc").
//
// IEntity is complex to fake (deep type-system interface), but the
// composite never introspects it — it just forwards. Our stub providers
// ignore the parameter and return a fixed string, so we can pass null
// safely. Real ICSharpCode.Decompiler IDocumentationProvider impls would
// reject null per the interface contract, but that's a separate concern.
public class IlSpyCompositeDocumentationProviderTests
{
    private sealed class StubProvider : IDocumentationProvider
    {
        private readonly string? myDoc;
        public StubProvider(string? doc) { myDoc = doc; }
        public string? GetDocumentation(IEntity entity) => myDoc;
    }

    [Fact]
    public void Empty_inner_list_returns_null()
    {
        IlSpyCompositeDocumentationProvider composite = new(new List<IDocumentationProvider>());
        Assert.Null(composite.GetDocumentation(null!));
    }

    [Fact]
    public void All_null_inner_returns_null()
    {
        IlSpyCompositeDocumentationProvider composite = new(new List<IDocumentationProvider>
        {
            new StubProvider(null),
            new StubProvider(null),
        });
        Assert.Null(composite.GetDocumentation(null!));
    }

    [Fact]
    public void First_non_null_wins()
    {
        IlSpyCompositeDocumentationProvider composite = new(new List<IDocumentationProvider>
        {
            new StubProvider(null),
            new StubProvider("<summary>from second</summary>"),
            new StubProvider("<summary>from third</summary>"),
        });
        Assert.Equal("<summary>from second</summary>", composite.GetDocumentation(null!));
    }

    [Fact]
    public void Empty_string_is_treated_as_missing()
    {
        // Defensive: a provider that returns "" instead of null shouldn't
        // shadow a later provider that actually has documentation.
        IlSpyCompositeDocumentationProvider composite = new(new List<IDocumentationProvider>
        {
            new StubProvider(""),
            new StubProvider("<summary>real doc</summary>"),
        });
        Assert.Equal("<summary>real doc</summary>", composite.GetDocumentation(null!));
    }

    [Fact]
    public void First_provider_with_hit_short_circuits()
    {
        IlSpyCompositeDocumentationProvider composite = new(new List<IDocumentationProvider>
        {
            new StubProvider("<summary>winner</summary>"),
            new StubProvider("<summary>loser</summary>"),
        });
        Assert.Equal("<summary>winner</summary>", composite.GetDocumentation(null!));
    }

    [Fact]
    public void BuildForAssembly_null_or_empty_returns_null()
    {
        Assert.Null(IlSpyCompositeDocumentationProvider.BuildForAssembly(null));
        Assert.Null(IlSpyCompositeDocumentationProvider.BuildForAssembly(""));
    }

    [Fact]
    public void BuildForAssembly_no_xml_on_disk_returns_null()
    {
        string root = Path.Combine(Path.GetTempPath(), "rider-ilspy-build-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string asmPath = Path.Combine(root, "Empty.dll");
            File.WriteAllText(asmPath, "");
            Assert.Null(IlSpyCompositeDocumentationProvider.BuildForAssembly(asmPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void BuildForAssembly_sidecar_only_returns_composite()
    {
        string root = Path.Combine(Path.GetTempPath(), "rider-ilspy-build-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string asmPath = Path.Combine(root, "WithDocs.dll");
            File.WriteAllText(asmPath, "");
            File.WriteAllText(Path.Combine(root, "WithDocs.xml"), "<doc><assembly><name>WithDocs</name></assembly><members></members></doc>");
            IDocumentationProvider? provider = IlSpyCompositeDocumentationProvider.BuildForAssembly(asmPath);
            Assert.NotNull(provider);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void BuildForAssembly_refpack_only_returns_composite()
    {
        // .NET shared-runtime impl with no sidecar but a populated ref pack —
        // mirrors the .NET 10 BCL situation where impl assemblies in shared/
        // ship without xmldocs but the parallel ref pack carries them.
        string root = Path.Combine(Path.GetTempPath(), "rider-ilspy-build-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string implDir = Path.Combine(root, "shared", "Microsoft.NETCore.App", "10.0.4");
            string refTfmDir = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref", "10.0.4", "ref", "net10.0");
            Directory.CreateDirectory(implDir);
            Directory.CreateDirectory(refTfmDir);
            string asmPath = Path.Combine(implDir, "System.Private.CoreLib.dll");
            File.WriteAllText(asmPath, "");
            File.WriteAllText(Path.Combine(refTfmDir, "System.Runtime.xml"), "<doc><assembly><name>System.Runtime</name></assembly><members></members></doc>");

            IDocumentationProvider? provider = IlSpyCompositeDocumentationProvider.BuildForAssembly(asmPath);
            Assert.NotNull(provider);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
