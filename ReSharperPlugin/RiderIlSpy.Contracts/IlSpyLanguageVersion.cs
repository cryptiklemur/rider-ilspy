namespace RiderIlSpy;

// Mirrors ICSharpCode.Decompiler.CSharp.LanguageVersion. Stored numerically
// so the persisted ordinal stays meaningful across ILSpy upgrades — the
// underlying enum uses sparse values (701, 800, 900, ...) that match the
// ILSpy convention. `Latest` is the sentinel meaning "let ILSpy pick" and
// is the default for new installs.
public enum IlSpyLanguageVersion
{
    Latest = 0,
    CSharp7_3 = 703,
    CSharp8_0 = 800,
    CSharp9_0 = 900,
    CSharp10_0 = 1000,
    CSharp11_0 = 1100,
    CSharp12_0 = 1200,
    CSharp13_0 = 1300,
    CSharp14_0 = 1400,
}
