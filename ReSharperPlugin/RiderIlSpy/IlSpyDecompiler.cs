using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        CSharpDecompiler decompiler = CreateDecompiler(assemblyPath, settings ?? new DecompilerSettings(), extraSearchDirs);
        return decompiler.DecompileModuleAndAssemblyAttributesToString();
    }

    private static string DecompileToCSharp(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        CSharpDecompiler decompiler = CreateDecompiler(assemblyPath, settings, extraSearchDirs);
        ITypeDefinition? type = decompiler.TypeSystem.MainModule.TopLevelTypeDefinitions
            .FirstOrDefault(t => t.FullName == typeFullName);
        if (type == null) return "// type not found: " + typeFullName;
        return decompiler.DecompileTypeAsString(new FullTypeName(typeFullName));
    }

    private static string DisassembleMixed(string assemblyPath, string typeFullName, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
            assemblyPath,
            settings.ThrowOnAssemblyResolveErrors,
            module.DetectTargetFrameworkId());
        if (extraSearchDirs != null)
            foreach (string dir in extraSearchDirs)
                resolver.AddSearchDirectory(dir);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        StringWriter sw = new StringWriter();
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
        PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
            assemblyPath,
            settings.ThrowOnAssemblyResolveErrors,
            module.DetectTargetFrameworkId());
        if (extraSearchDirs != null)
            foreach (string dir in extraSearchDirs)
                resolver.AddSearchDirectory(dir);

        TypeDefinitionHandle handle = FindTypeHandle(module, typeFullName);
        if (handle.IsNil) return "// type not found: " + typeFullName;

        StringWriter sw = new StringWriter();
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

    private static TypeDefinitionHandle FindTypeHandle(PEFile module, string typeFullName)
    {
        MetadataReader metadata = module.Metadata;
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition def = metadata.GetTypeDefinition(handle);
            string ns = metadata.GetString(def.Namespace);
            string name = metadata.GetString(def.Name);
            string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            if (full == typeFullName) return handle;
        }
        return default;
    }

    private static CSharpDecompiler CreateDecompiler(string assemblyPath, DecompilerSettings settings, IReadOnlyList<string>? extraSearchDirs)
    {
        if (extraSearchDirs == null || extraSearchDirs.Count == 0)
            return new CSharpDecompiler(assemblyPath, settings);

        PEFile module = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
        UniversalAssemblyResolver resolver = new UniversalAssemblyResolver(
            assemblyPath,
            settings.ThrowOnAssemblyResolveErrors,
            module.DetectTargetFrameworkId());
        foreach (string dir in extraSearchDirs)
            resolver.AddSearchDirectory(dir);
        return new CSharpDecompiler(module, resolver, settings);
    }
}
