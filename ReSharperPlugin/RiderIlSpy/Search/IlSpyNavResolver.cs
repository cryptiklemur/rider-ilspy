using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace RiderIlSpy.Search;

public sealed class IlSpyNavResolution
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; } = 1;
    public int Column { get; init; } = 1;
    public string ErrorMessage { get; init; } = string.Empty;

    public static IlSpyNavResolution Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static IlSpyNavResolution Ok(string path, int line, int column) =>
        new() { Success = true, FilePath = path, Line = line, Column = column };
}

public sealed class IlSpyNavResolver
{
    private readonly string myCacheRoot;

    public IlSpyNavResolver(string? cacheRoot = null)
    {
        myCacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "RiderIlSpy", "search-nav");
    }

    public IlSpyNavResolution Resolve(string assemblyPath, int metadataToken, int ilOffset)
    {
        try
        {
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
                return IlSpyNavResolution.Failure($"assembly not found: {assemblyPath}");

            using PEFile module = new(assemblyPath, PEStreamOptions.PrefetchEntireImage, MetadataReaderOptions.Default);
            EntityHandle entityHandle = MetadataTokens.EntityHandle(metadataToken);
            if (entityHandle.IsNil)
                return IlSpyNavResolution.Failure($"invalid metadata token: 0x{metadataToken:X8}");

            TypeDefinitionHandle typeHandle = ResolveContainingType(module.Metadata, entityHandle);
            if (typeHandle.IsNil)
                return IlSpyNavResolution.Failure($"could not resolve containing type for token 0x{metadataToken:X8}");

            try
            {
                return TryDecompileCSharp(assemblyPath, module, entityHandle, typeHandle, ilOffset);
            }
            catch (ArgumentException tfmBugEx) when (DecompilerTypeSystemPatch.IsTwoComponentTfmVersionBug(tfmBugEx))
            {
                // Hit the upstream 2-component TFM bug (e.g. net10.0). Apply the
                // process-wide neuter and retry once; if neuter or retry fails,
                // surface IL disassembly so the user still navigates somewhere
                // usable instead of seeing an unhandled exception in Rider.
                return RetryAfterTfmFix(assemblyPath, module, entityHandle, typeHandle, ilOffset, tfmBugEx);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ilspy-search-nav] resolve failed path={assemblyPath} token=0x{metadataToken:X8} ilOffset={ilOffset}\n{ex}");
            return IlSpyNavResolution.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private IlSpyNavResolution RetryAfterTfmFix(string assemblyPath, PEFile module, EntityHandle entityHandle, TypeDefinitionHandle typeHandle, int ilOffset, ArgumentException original)
    {
        if (!DecompilerTypeSystemPatch.TryNeuter())
            return DisassembleIlFallback(assemblyPath, module, entityHandle, typeHandle, original);
        try
        {
            return TryDecompileCSharp(assemblyPath, module, entityHandle, typeHandle, ilOffset);
        }
        catch (Exception retryEx)
        {
            Console.Error.WriteLine($"[ilspy-search-nav] retry-after-neuter failed; falling back to IL. path={assemblyPath}\n{retryEx}");
            return DisassembleIlFallback(assemblyPath, module, entityHandle, typeHandle, retryEx);
        }
    }

    private IlSpyNavResolution TryDecompileCSharp(string assemblyPath, PEFile module, EntityHandle entityHandle, TypeDefinitionHandle typeHandle, int ilOffset)
    {
        string? tfm = module.DetectTargetFrameworkId();
        UniversalAssemblyResolver resolver = new(assemblyPath, throwOnError: false, tfm);
        DecompilerSettings settings = new() { ShowXmlDocumentation = true };
        CSharpDecompiler decompiler = new(module, resolver, settings);
        // Mirror IlSpyDecompiler.DecompileToCSharp: when ShowXmlDocumentation
        // is on, attach the composite xmldoc provider so search-driven
        // navigation gets the same `///` comments as the regular decompile
        // path. Without this, search-result decompiles silently drop xmldocs
        // even on assemblies that ship them.
        IDocumentationProvider? docProvider = IlSpyCompositeDocumentationProvider.BuildForAssembly(assemblyPath);
        if (docProvider != null) decompiler.DocumentationProvider = docProvider;

        ITypeDefinition? typeDef = decompiler.TypeSystem.MainModule.GetDefinition(typeHandle);
        if (typeDef == null)
            return IlSpyNavResolution.Failure(RiderIlSpy.Resources.Strings.NavResolver_TypeDefinitionNotFound);

        SyntaxTree tree = decompiler.DecompileType(typeDef.FullTypeName);
        string body = SyntaxTreeToString(tree, settings);

        int line = 1;
        int column = 1;
        if (entityHandle.Kind == HandleKind.MethodDefinition)
        {
            (line, column) = FindLineForMethod(decompiler, tree, (MethodDefinitionHandle)entityHandle, ilOffset);
        }
        if (line < 1) line = 1;
        if (column < 1) column = 1;

        string banner = BuildBanner(assemblyPath, typeDef.FullTypeName.ReflectionName, IlSpyOutputMode.CSharp);
        string text = banner + body;
        line += CountLines(banner);

        string cachePath = WriteCacheFile(assemblyPath, typeDef.FullTypeName.ReflectionName, ".cs", text);
        return IlSpyNavResolution.Ok(cachePath, line, column);
    }

    private IlSpyNavResolution DisassembleIlFallback(string assemblyPath, PEFile module, EntityHandle entityHandle, TypeDefinitionHandle typeHandle, Exception cause)
    {
        TypeDefinition typeDef = module.Metadata.GetTypeDefinition(typeHandle);
        string typeNamespace = module.Metadata.GetString(typeDef.Namespace);
        string typeName = module.Metadata.GetString(typeDef.Name);
        string fullTypeName = string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";

        using StringWriter sw = new();
        PlainTextOutput output = new(sw);
        ReflectionDisassembler dis = new(output, default) { ShowSequencePoints = false };
        dis.DisassembleType(module, typeHandle);
        string ilBody = sw.ToString();

        int line = 1;
        if (entityHandle.Kind == HandleKind.MethodDefinition)
        {
            string methodName = module.Metadata.GetString(
                module.Metadata.GetMethodDefinition((MethodDefinitionHandle)entityHandle).Name);
            line = FindMethodLineInIl(ilBody, methodName);
        }
        if (line < 1) line = 1;

        StringBuilder banner = new(512);
        banner.Append(BuildBanner(assemblyPath, fullTypeName, IlSpyOutputMode.IL));
        banner.Append("// RiderIlSpy: C# decompile failed; falling back to IL disassembly.\n");
        banner.Append("// Reason: ").Append(cause.GetType().Name).Append(": ").Append(cause.Message).Append('\n');
        banner.Append('\n');
        string bannerText = banner.ToString();
        line += CountLines(bannerText);

        string cachePath = WriteCacheFile(assemblyPath, fullTypeName, ".il", bannerText + ilBody);
        return IlSpyNavResolution.Ok(cachePath, line, 1);
    }

    private static string BuildBanner(string assemblyPath, string typeFullName, IlSpyOutputMode mode)
    {
        AssemblyBannerMetadata? meta = IlSpyExternalSourcesProviderHelpers.ReadAssemblyBannerMetadata(assemblyPath);
        BannerContext ctx = new(meta, assemblyPath, typeFullName, mode, Array.Empty<string>());
        return IlSpyExternalSourcesProviderHelpers.BuildDiagnosticBanner(ctx, sourceLinkOutcome: null);
    }

    private static int CountLines(string s)
    {
        int n = 0;
        foreach (char c in s)
            if (c == '\n') n++;
        return n;
    }

    private static int FindMethodLineInIl(string ilText, string methodName)
    {
        string needle = " " + methodName + "(";
        int idx = ilText.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0) return 1;
        int line = 1;
        for (int i = 0; i < idx; i++)
            if (ilText[i] == '\n') line++;
        return line;
    }

    private static TypeDefinitionHandle ResolveContainingType(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return (TypeDefinitionHandle)handle;
            case HandleKind.MethodDefinition:
                MethodDefinition method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return method.GetDeclaringType();
            case HandleKind.FieldDefinition:
                FieldDefinition field = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return field.GetDeclaringType();
            case HandleKind.PropertyDefinition:
                PropertyDefinition property = metadata.GetPropertyDefinition((PropertyDefinitionHandle)handle);
                return ResolveAccessorParent(metadata, property.GetAccessors());
            case HandleKind.EventDefinition:
                EventDefinition evt = metadata.GetEventDefinition((EventDefinitionHandle)handle);
                return ResolveAccessorParent(metadata, evt.GetAccessors());
            default:
                return default;
        }
    }

    private static TypeDefinitionHandle ResolveAccessorParent(MetadataReader metadata, PropertyAccessors accessors)
    {
        MethodDefinitionHandle anyAccessor = !accessors.Getter.IsNil ? accessors.Getter : accessors.Setter;
        if (anyAccessor.IsNil) return default;
        return metadata.GetMethodDefinition(anyAccessor).GetDeclaringType();
    }

    private static TypeDefinitionHandle ResolveAccessorParent(MetadataReader metadata, EventAccessors accessors)
    {
        MethodDefinitionHandle anyAccessor = !accessors.Adder.IsNil ? accessors.Adder
            : !accessors.Remover.IsNil ? accessors.Remover
            : accessors.Raiser;
        if (anyAccessor.IsNil) return default;
        return metadata.GetMethodDefinition(anyAccessor).GetDeclaringType();
    }

    private static string SyntaxTreeToString(SyntaxTree tree, DecompilerSettings settings)
    {
        // CreateWriterThatSetsLocationsInAST does double duty: writes the formatted
        // text to `sw` AND populates each AST node's StartLocation/EndLocation
        // as it goes. We need the latter so FindMemberDeclaration can map a
        // metadata token to its line in the output. The plain CSharpOutputVisitor
        // overload that takes a TextWriter does NOT populate AST positions — every
        // node stays at (0,0), and the resolver lands the user on line 1 plus
        // banner offset. Indent comes from the decompiler settings so the output
        // looks like JetBrains-style C# (tabs / 4-space indents per user pref).
        using StringWriter sw = new();
        TokenWriter tokenWriter = TokenWriter.CreateWriterThatSetsLocationsInAST(
            sw, settings.CSharpFormattingOptions.IndentationString);
        tree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
        return sw.ToString();
    }

    private static (int line, int column) FindLineForMethod(
        CSharpDecompiler decompiler,
        SyntaxTree tree,
        MethodDefinitionHandle target,
        int ilOffset)
    {
        // AST walk is the source of truth for "where does this method live in the
        // decompiled text". Sequence-point lookup via CreateSequencePoints is a
        // refinement on top — it can pinpoint the specific statement inside the
        // body for a given ilOffset, but it can't be the primary path: in
        // practice the ILFunction.Method.MetadataToken comparison misses for
        // common cases (no entry in the dict for the requested method), and the
        // fallback was landing on line 1, i.e. the start of the banner.
        EntityDeclaration? decl = FindMemberDeclaration(tree, target);
        int declLine = decl?.StartLocation.Line ?? 1;
        int declCol = decl?.StartLocation.Column ?? 1;

        try
        {
            Dictionary<ILFunction, List<SequencePoint>> all = decompiler.CreateSequencePoints(tree);
            foreach (KeyValuePair<ILFunction, List<SequencePoint>> kv in all)
            {
                EntityHandle methodHandle = kv.Key.Method?.MetadataToken ?? default;
                if (methodHandle != (EntityHandle)target) continue;
                SequencePoint? best = PickSequencePoint(kv.Value, ilOffset);
                if (best != null)
                    return (best.StartLine, best.StartColumn);
            }
        }
        catch
        {
            /* fall through to the AST-derived line */
        }

        return (declLine, declCol);
    }

    private static EntityDeclaration? FindMemberDeclaration(SyntaxTree tree, EntityHandle target)
    {
        foreach (AstNode node in tree.DescendantsAndSelf)
        {
            if (node is not EntityDeclaration decl) continue;
            IEntity? entity = decl.GetSymbol() as IEntity ?? decl.Annotation<IEntity>();
            if (entity != null && entity.MetadataToken == target)
                return decl;
        }
        return null;
    }

    private static SequencePoint? PickSequencePoint(IList<SequencePoint> points, int ilOffset)
    {
        if (points.Count == 0) return null;
        if (ilOffset < 0) return FirstVisible(points);

        foreach (SequencePoint p in points)
        {
            if (IsUnusable(p)) continue;
            if (p.Offset <= ilOffset && ilOffset < p.EndOffset) return p;
        }

        SequencePoint? closest = null;
        int closestOffset = -1;
        foreach (SequencePoint p in points)
        {
            if (IsUnusable(p)) continue;
            if (p.Offset <= ilOffset && p.Offset > closestOffset)
            {
                closest = p;
                closestOffset = p.Offset;
            }
        }
        return closest ?? FirstVisible(points);
    }

    private static SequencePoint? FirstVisible(IList<SequencePoint> points)
    {
        foreach (SequencePoint p in points)
            if (!IsUnusable(p)) return p;
        return null;
    }

    private static bool IsUnusable(SequencePoint p) => p.IsHidden || p.StartLine <= 0 || p.StartLine >= 0xFEEFEE;

    private string WriteCacheFile(string assemblyPath, string typeReflectionName, string extension, string content)
    {
        string assemblyHash = HashKey(assemblyPath);
        string dir = Path.Combine(myCacheRoot, assemblyHash);
        Directory.CreateDirectory(dir);
        string fileName = SanitizeForFilename(typeReflectionName) + extension;
        string fullPath = Path.Combine(dir, fileName);
        // Always overwrite. The hash key is derived from the assembly's mtime, so a
        // re-decompile of an unchanged assembly produces the same key and would
        // hit a stale cache from a previous plugin build (e.g. an older formatter
        // version). Decompile is the expensive part; writing a few KB of text on
        // top of an existing file is negligible compared to the cost of a user-
        // visible "why is my code still misformatted after the fix?" debugging
        // session.
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return fullPath;
    }

    private static string HashKey(string assemblyPath)
    {
        long ticks = 0;
        try { ticks = File.GetLastWriteTimeUtc(assemblyPath).Ticks; }
        catch { /* hashed on path alone */ }
        string raw = assemblyPath.ToLowerInvariant() + "|" + ticks;
        byte[] bytes = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        StringBuilder sb = new(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString()[..16];
    }

    private static string SanitizeForFilename(string raw)
    {
        StringBuilder sb = new(raw.Length);
        foreach (char c in raw)
        {
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
