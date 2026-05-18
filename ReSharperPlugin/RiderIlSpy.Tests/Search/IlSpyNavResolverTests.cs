using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ICSharpCode.Decompiler.Metadata;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpyNavResolverTests
{
    [Fact]
    public void Resolves_Method_To_Decompiled_File_With_Line()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        Assert.True(File.Exists(assemblyPath), $"fixture missing: {assemblyPath}");

        int helloToken = FindMethodToken(assemblyPath, "Greetings", "Hello");
        Assert.NotEqual(0, helloToken);

        string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-Nav", Guid.NewGuid().ToString("N"));
        IlSpyNavResolver resolver = new IlSpyNavResolver(cacheRoot);

        IlSpyNavResolution result = resolver.Resolve(assemblyPath, helloToken, ilOffset: 0);

        try
        {
            if (!result.Success) Assert.Fail($"resolver failed: {result.ErrorMessage}");
            if (!File.Exists(result.FilePath)) Assert.Fail($"cache file not written: {result.FilePath}");
            string content = File.ReadAllText(result.FilePath);
            Assert.Contains("Hello, world.", content);
            Assert.Contains("Hello", content);
            Assert.True(result.Line >= 1);
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Decompiled_Output_Is_Properly_Indented_And_Has_Banner()
    {
        // Regression for a formatter bug that landed in the sandbox: the helper
        // passed "\n" as the indent string to TokenWriter.Create, which made the
        // output have zero indentation and a blank line for every indent level.
        // The user saw class members "spread out" with no inner indentation.
        //
        // Also asserts the diagnostic banner shape (the user noticed the banner
        // was missing on first run) and that the returned line lands ON or AFTER
        // the banner-end blank line so the offset accounts for the prepended text.
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        Assert.True(File.Exists(assemblyPath), $"fixture missing: {assemblyPath}");

        int helloToken = FindMethodToken(assemblyPath, "Greetings", "Hello");
        Assert.NotEqual(0, helloToken);

        string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-Nav", Guid.NewGuid().ToString("N"));
        IlSpyNavResolver resolver = new IlSpyNavResolver(cacheRoot);
        IlSpyNavResolution result = resolver.Resolve(assemblyPath, helloToken, ilOffset: 0);

        try
        {
            Assert.True(result.Success, $"resolver failed: {result.ErrorMessage}");
            string content = File.ReadAllText(result.FilePath);

            Assert.StartsWith("// Decompiled with RiderIlSpy", content);
            Assert.Contains("// Type: ", content);
            Assert.Contains("// Mode: CSharp", content);

            // Indentation sanity: the method body line ("return ...") must be
            // preceded by whitespace. If the formatter regresses to "\n" as the
            // indent, this line would have no leading whitespace at all.
            string[] lines = content.Split('\n');
            string? returnLine = null;
            foreach (string l in lines) if (l.TrimStart().StartsWith("return ")) { returnLine = l; break; }
            Assert.NotNull(returnLine);
            Assert.True(returnLine!.StartsWith(" ") || returnLine.StartsWith("\t"),
                $"method body should be indented; got: \"{returnLine}\"");

            // Blank-line sanity: with the broken formatter, the output had three
            // or more consecutive empty lines between every member. Allow up to
            // two (one trailing newline from a member, one blank separator).
            int maxRun = 0, run = 0;
            foreach (string l in lines)
            {
                if (l.Length == 0) { run++; if (run > maxRun) maxRun = run; }
                else run = 0;
            }
            Assert.True(maxRun <= 2, $"output has runs of {maxRun} blank lines (formatter likely regressed)");

            // The returned line must fall AFTER the banner block — the banner ends
            // at the first blank line, after which the actual code starts.
            int bannerEndLine = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) { bannerEndLine = i + 1; break; }
            }
            Assert.True(bannerEndLine > 0, "could not find end of banner block");
            Assert.True(result.Line > bannerEndLine,
                $"returned line {result.Line} should be past banner end (line {bannerEndLine})");
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Returns_Failure_For_Missing_Assembly()
    {
        IlSpyNavResolver resolver = new IlSpyNavResolver();
        IlSpyNavResolution result = resolver.Resolve("/nonexistent/missing.dll", 0x06000001, ilOffset: 0);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public void Returns_Failure_For_Invalid_Token()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        IlSpyNavResolver resolver = new IlSpyNavResolver();

        IlSpyNavResolution result = resolver.Resolve(assemblyPath, metadataToken: 0, ilOffset: 0);

        Assert.False(result.Success);
    }

    [Fact]
    public void Returned_Line_Lands_Inside_The_Requested_Method_Body()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        Assert.True(File.Exists(assemblyPath), $"fixture missing: {assemblyPath}");
        int helloToken = FindMethodToken(assemblyPath, "Greetings", "Hello");
        string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-Nav", Guid.NewGuid().ToString("N"));
        IlSpyNavResolver resolver = new IlSpyNavResolver(cacheRoot);
        try
        {
            IlSpyNavResolution result = resolver.Resolve(assemblyPath, helloToken, ilOffset: 0);
            Assert.True(result.Success);
            string[] lines = File.ReadAllLines(result.FilePath);
            Assert.True(result.Line >= 1 && result.Line <= lines.Length,
                $"line {result.Line} out of range (file has {lines.Length} lines)");
            // Method is `void Hello() { Console.WriteLine("Hello, world."); }`.
            // For ilOffset=0 the resolver should land us either on the method
            // signature or on the first body statement — anything else (e.g.
            // landing on the namespace, a using, or a banner row) means the
            // sequence-point→line mapping is broken.
            string landedLine = lines[result.Line - 1];
            int scanFrom = Math.Max(0, result.Line - 3);
            int scanTo = Math.Min(lines.Length, result.Line + 2);
            string window = string.Join("\n", lines[scanFrom..scanTo]);
            Assert.True(
                window.Contains("Hello") || window.Contains("Console.WriteLine"),
                $"resolver returned line {result.Line} (\"{landedLine}\"); surrounding window:\n{window}");
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Cache_File_Is_Overwritten_On_Second_Resolve_For_Same_Assembly()
    {
        // Regression for a real user-visible bug: the cache file was written with
        // "if not exists" semantics, so once a (broken) decompile result landed
        // on disk, every subsequent Resolve returned the stale content even after
        // the plugin was rebuilt with a fix. Surfaced as "no improvement after
        // rebuild" because the new code never executed — File.Exists short-
        // circuited the write. Contract: cache must always reflect the latest
        // decompile output, not the first one ever written.
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "literals.dll");
        Assert.True(File.Exists(assemblyPath), $"fixture missing: {assemblyPath}");
        int helloToken = FindMethodToken(assemblyPath, "Greetings", "Hello");

        string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-Nav", Guid.NewGuid().ToString("N"));
        IlSpyNavResolver resolver = new IlSpyNavResolver(cacheRoot);

        try
        {
            IlSpyNavResolution first = resolver.Resolve(assemblyPath, helloToken, ilOffset: 0);
            Assert.True(first.Success);

            // Poison the cache: simulate a "broken" prior decompile output that
            // should be replaced on the next resolve.
            File.WriteAllText(first.FilePath, "// STALE BROKEN CONTENT — must not survive next resolve\n");

            IlSpyNavResolution second = resolver.Resolve(assemblyPath, helloToken, ilOffset: 0);
            Assert.True(second.Success);
            Assert.Equal(first.FilePath, second.FilePath);
            string content = File.ReadAllText(second.FilePath);
            Assert.DoesNotContain("STALE BROKEN CONTENT", content);
            Assert.Contains("Hello, world.", content);
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Survives_Decompiler_TwoComponent_TFM_Bug_Without_Crashing()
    {
        // Regression for the upstream ICSharpCode.Decompiler bug:
        // DecompilerTypeSystem.InitializeAsync hard-codes Version.ToString(3) when
        // synthesising implicit-reference names, which throws ArgumentException for
        // any .NETStandard/.NETCoreApp/.NET TFM whose Version has fewer than 3
        // components and whose Major is >= 10 (because ParseTargetFramework only
        // pads "x.y" → "x.y.0" when the version string is exactly 3 chars).
        // Concrete trigger: net10.0 (TFM "v10.0" — length 4, no pad → Version(10,0)).
        //
        // Contract: Resolve() must never surface this exception to the caller — the
        // user gets a usable navigation target (C# source via the post-neuter retry
        // path, or IL disassembly via the last-resort fallback). Which path fires
        // depends on whether DecompilerTypeSystemPatch.TryNeuter has already run in
        // this process; both are valid outcomes from the caller's point of view.
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "fieldcount_bug_net10.dll");
        Assert.True(File.Exists(assemblyPath), $"fixture missing: {assemblyPath}");

        int triggerToken = FindMethodToken(assemblyPath, "TriggersBug", "Trigger");
        Assert.NotEqual(0, triggerToken);

        string cacheRoot = Path.Combine(Path.GetTempPath(), "RiderIlSpyTests-Nav", Guid.NewGuid().ToString("N"));
        IlSpyNavResolver resolver = new IlSpyNavResolver(cacheRoot);

        IlSpyNavResolution result = resolver.Resolve(assemblyPath, triggerToken, ilOffset: 0);

        try
        {
            if (!result.Success) Assert.Fail($"resolver crashed instead of recovering: {result.ErrorMessage}");
            if (!File.Exists(result.FilePath)) Assert.Fail($"navigation file not written: {result.FilePath}");
            string content = File.ReadAllText(result.FilePath);
            Assert.Contains("Trigger", content);
            Assert.True(result.Line >= 1);
        }
        finally
        {
            try { Directory.Delete(cacheRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static int FindMethodToken(string assemblyPath, string typeShortName, string methodName)
    {
        using PEFile module = new PEFile(assemblyPath);
        MetadataReader metadata = module.Metadata;
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition typeDef = metadata.GetTypeDefinition(typeHandle);
            string name = metadata.GetString(typeDef.Name);
            if (name != typeShortName) continue;
            foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods())
            {
                MethodDefinition methodDef = metadata.GetMethodDefinition(methodHandle);
                if (metadata.GetString(methodDef.Name) == methodName)
                    return MetadataTokens.GetToken(methodHandle);
            }
        }
        return 0;
    }
}
