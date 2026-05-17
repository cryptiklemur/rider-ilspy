# Test Fixture Assemblies

Pre-compiled DLLs used by RiderIlSpy.Tests to exercise the assembly-search index without
depending on production assemblies at test time.

## Contents

| File | Fixture source | Purpose |
|------|---------------|---------|
| `literals.dll` | `Source/Literals.cs` | String literal search |
| `attributes.dll` | `Source/Attributes.cs` | Attribute / [Obsolete] search |
| `resources.dll` | `Source/Resources.cs` + `embedded.txt` | Embedded resource search |

## Rebuilding

Requires Mono `csc` (or any C# 5-compatible compiler):

```bash
cd ReSharperPlugin/RiderIlSpy.Tests/TestFixtures
csc -target:library -out:literals.dll Source/Literals.cs
csc -target:library -out:attributes.dll Source/Attributes.cs
csc -target:library -out:resources.dll -resource:embedded.txt Source/Resources.cs
```

Commit the resulting `.dll` files. They are intentionally checked in as binaries so that tests
run without requiring a compiler at test time.
