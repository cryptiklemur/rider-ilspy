# Test Fixture Assemblies

Pre-compiled DLLs used by RiderIlSpy.Tests to exercise the assembly-search index without
depending on production assemblies at test time.

## Contents

| File | Fixture source | Purpose |
|------|---------------|---------|
| `literals.dll` | `Source/Literals.cs` | String literal search |
| `attributes.dll` | `Source/Attributes.cs` | Attribute / [Obsolete] search |
| `constants.dll` | `Source/Constants.cs` | `Constant` metadata table — one row per `ConstantTypeCode` branch (Int32 / Int64 / Boolean / String / Char / Double / Single) consumed by `ConstantQueryHandler.DecodeConstant` |
| `resources.dll` | `Source/Resources.cs` + `embedded.txt` | Embedded resource search |
| `embedded.txt` | (input) | Plain-text resource embedded into `resources.dll` via `-resource:` |
| `fieldcount_bug_net10.dll` | `Source/FieldCountBug.cs` | .NET 10 TFM (2-part version) — triggers upstream ICSharpCode `Version.ToString(3)` bug in `DecompilerTypeSystem.InitializeAsync`. Used by `IlSpyNavResolverTests.Falls_Back_To_IL_For_Decompiler_FieldCount_Bug` to assert the IL-disassembly fallback path. |

The fixture `.cs` files in `Source/` are excluded from `RiderIlSpy.Tests` compilation
(see `<Compile Remove="TestFixtures\**\*.cs" />` in the test csproj). They exist
only as input to the standalone `csc` invocations below — the fixture types must
live ONLY in their respective DLLs so the indexer tests can prove discovery
through the DLL, not through the test assembly itself.

## Rebuilding

Requires Mono `csc` (or any C# 9-compatible compiler). Sources use block-scoped
namespaces because the Mono `csc` shipped on Arch (Roslyn 3.9) does not accept
file-scoped namespaces even with `-langversion:latest`.

```bash
cd ReSharperPlugin/RiderIlSpy.Tests/TestFixtures
csc -target:library -out:literals.dll Source/Literals.cs
csc -target:library -out:attributes.dll Source/Attributes.cs
csc -target:library -out:constants.dll Source/Constants.cs
csc -target:library -out:resources.dll -resource:embedded.txt Source/Resources.cs
```

`fieldcount_bug_net10.dll` is built via the .NET SDK (it must target .NET 10
specifically to reproduce the upstream bug — Mono `csc` cannot emit a .NET 10
TFM attribute). Rebuild with:

```bash
mkdir -p /tmp/fieldcount-bug && cd /tmp/fieldcount-bug
cat > Bug.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>fieldcount_bug_net10</AssemblyName>
  </PropertyGroup>
</Project>
EOF
cp <path-to-repo>/ReSharperPlugin/RiderIlSpy.Tests/TestFixtures/Source/FieldCountBug.cs Bug.cs
dotnet build -c Release -o out
cp out/fieldcount_bug_net10.dll <path-to-repo>/ReSharperPlugin/RiderIlSpy.Tests/TestFixtures/
```

Commit the resulting `.dll` files. They are intentionally checked in as binaries so that tests
run without requiring a compiler at test time.
