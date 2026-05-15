using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using JetBrains.Application;

namespace RiderIlSpy;

[ShellComponent]
public class IlSpyDecompiler
{
    public string DecompileType(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs = null, IlSpyOutputMode mode = IlSpyOutputMode.CSharp)
    {
        try
        {
            switch (mode)
            {
                case IlSpyOutputMode.IL:
                    return DisassembleToIl(assemblyPath, typeFullName, settings, extraSearchDirs);
                case IlSpyOutputMode.CSharpWithIL:
                    return DisassembleMixed(assemblyPath, typeFullName, settings, extraSearchDirs);
                default:
                    return DecompileToCSharp(assemblyPath, typeFullName, settings, extraSearchDirs);
            }
        }
        catch (System.Exception ex)
        {
            return "// RiderIlSpy decompile failed for " + typeFullName + "\n// " + ex.GetType().FullName + ": " + ex.Message;
        }
    }

    public string DecompileAssemblyInfo(string assemblyPath, DecompilerSettings? settings = null, IReadOnlyList<string>? extraSearchDirs = null)
    {
        DecompilerSettings effective = settings ?? new DecompilerSettings();
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, effective, extraSearchDirs);
        CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, effective);
        return decompiler.DecompileModuleAndAssemblyAttributesToString();
    }

    private static string DecompileToCSharp(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);
        CSharpDecompiler decompiler = new CSharpDecompiler(module, resolver, settings);

        FullTypeName ftn;
        try { ftn = new FullTypeName(typeFullName); }
        catch { return "// invalid type name: " + typeFullName; }

        ITypeDefinition? type = decompiler.TypeSystem.MainModule.GetTypeDefinition(ftn);
        if (type == null) return "// type not found: " + typeFullName;
        return decompiler.DecompileTypeAsString(ftn);
    }

    private static string DisassembleMixed(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        using StringWriter sw = new StringWriter();
        PlainTextOutput output = new PlainTextOutput(sw);
        MixedMethodBodyDisassembler bodyDisassembler = new MixedMethodBodyDisassembler(output, CancellationToken.None, settings, resolver)
        {
            DetectControlStructure = true,
            ShowSequencePoints = false,
        };
        ReflectionDisassembler disassembler = new ReflectionDisassembler(output, bodyDisassembler, CancellationToken.None)
        {
            AssemblyResolver = resolver,
            DetectControlStructure = true,
            ShowSequencePoints = false,
            ExpandMemberDefinitions = true,
        };
        disassembler.DisassembleType(module, handle);
        return sw.ToString();
    }

    private static string DisassembleToIl(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        using PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = BuildResolver(assemblyPath, module, settings, extraSearchDirs);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        using StringWriter sw = new StringWriter();
        PlainTextOutput output = new PlainTextOutput(sw);
        ReflectionDisassembler disassembler = new ReflectionDisassembler(output, CancellationToken.None)
        {
            AssemblyResolver = resolver,
            DetectControlStructure = true,
            ShowSequencePoints = false,
            ExpandMemberDefinitions = true,
        };
        disassembler.DisassembleType(module, handle);
        return sw.ToString();
    }

    private static UniversalAssemblyResolver BuildResolver(string assemblyPath, PEFile module, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
            assemblyPath,
            settings.ThrowOnAssemblyResolveErrors,
            module.DetectTargetFrameworkId());
        if (extraSearchDirs != null)
            foreach (string dir in extraSearchDirs)
                resolver.AddSearchDirectory(dir);
        return resolver;
    }

    private static TypeDefinitionHandle FindTypeHandle(PEFile module, string typeFullName)
    {
        MetadataReader metadata = module.Metadata;
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition def = metadata.GetTypeDefinition(handle);
            string built = BuildTypeFullName(metadata, def);
            if (built == typeFullName) return handle;
        }
        return default;
    }

    // Builds a CLR-reflection-style full name: `Namespace.Outer+Inner+Leaf` with generic arity
    // baked into each segment (`List`1`). Rider's IClrTypeName.FullName uses the same shape.
    private static string BuildTypeFullName(MetadataReader metadata, TypeDefinition def)
    {
        string name = metadata.GetString(def.Name);
        TypeDefinitionHandle declaringHandle = def.GetDeclaringType();
        if (!declaringHandle.IsNil)
        {
            TypeDefinition decl = metadata.GetTypeDefinition(declaringHandle);
            return BuildTypeFullName(metadata, decl) + "+" + name;
        }
        string ns = metadata.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
