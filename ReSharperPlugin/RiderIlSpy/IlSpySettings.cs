using JetBrains.Application.Settings;
using JetBrains.Application.Settings.WellKnownRootKeys;

namespace RiderIlSpy;

public enum IlSpyOutputMode
{
    CSharp = 0,
    IL = 1,
    CSharpWithIL = 2,
}

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
}

[SettingsKey(typeof(EnvironmentSettings), "ILSpy decompiler settings")]
public class IlSpySettings
{
    [SettingsEntry(true, "Enable ILSpy as the external decompiler")]
    public bool Enabled;

    [SettingsEntry(IlSpyOutputMode.CSharp, "Default output mode")]
    public IlSpyOutputMode OutputMode;

    [SettingsEntry(false, "Show diagnostic banner at the top of decompiled output")]
    public bool ShowDiagnosticBanner;

    [SettingsEntry("", "Extra directories to search for runtime assemblies, separated by the platform path separator (';' on Windows, ':' on Linux/macOS)")]
    public string AssemblyResolveDirs = "";

    // When false (default), the provider refuses to produce decompiled output for
    // reference assemblies (paths containing the `/ref/` SDK marker — e.g. NuGet
    // ref packs like krafs.rimworld.ref, or compile-time .NETCore.App refs).
    // Refusing returns an empty NavigateToSources result, which lets Rider's
    // built-in dotPeek pick up the navigation instead — the user still
    // ctrl+clicks into the type, just without ILSpy's ref-only stub output.
    // Enable when the user explicitly wants to inspect the publicised/wrapped
    // surface of a ref pack (e.g. seeing what krafs exposes after publicization).
    [SettingsEntry(false, "Decompile reference assemblies (ref packs / publicised wrappers) instead of deferring to Rider's built-in decompiler")]
    public bool DecompileReferenceAssemblies;

    [SettingsEntry(true, "Use async/await syntax")]
    public bool AsyncAwait;

    [SettingsEntry(true, "Use expression-bodied members")]
    public bool ExpressionBodies;

    [SettingsEntry(true, "Use named arguments")]
    public bool NamedArguments;

    [SettingsEntry(true, "Show XML documentation comments")]
    public bool ShowXmlDocumentation;

    [SettingsEntry(true, "Remove dead code")]
    public bool RemoveDeadCode;

    [SettingsEntry(false, "Throw on assembly resolve errors instead of producing best-effort output")]
    public bool ThrowOnAssemblyResolveErrors;

    // Disabled by default so Rider's go-to-definition resolves record-struct
    // properties — positional primary-ctor syntax dumps callers at the top of
    // the file instead of the parameter site. Re-enable for a more compact
    // record syntax when navigation correctness isn't needed.
    [SettingsEntry(false, "Use primary constructor syntax with records (disable for go-to-definition correctness)")]
    public bool UsePrimaryConstructorSyntax;

    // Downgrades decompiler output to an older language level. Useful when
    // browsing assemblies built before a given C# version — modern syntax
    // (records, init accessors, file-scoped namespaces, raw strings) gets
    // back-rewritten to its pre-feature equivalent. Defaults to Latest so
    // existing installs see no change.
    [SettingsEntry(IlSpyLanguageVersion.Latest, "Target C# language version for decompiled output")]
    public IlSpyLanguageVersion LanguageVersion;

    // When enabled and the assembly's PDB carries a SourceLink CustomDebugInformation
    // entry, the backend fetches the original source from the published URL
    // (typically a Git host like raw.githubusercontent.com) instead of running
    // ILSpy. Improves fidelity dramatically for libraries that ship SourceLink
    // (.NET BCL, most NuGet packages built after ~2020). Off-by-default would
    // be surprising for users who have come to expect SourceLink in
    // dotPeek/Visual Studio.
    [SettingsEntry(true, "Prefer original source via SourceLink when available")]
    public bool PreferSourceLink;

    // Network timeout for SourceLink source fetches, in seconds. A short
    // timeout matters more than a long one — when the user is offline or the
    // host is unreachable we want to fall back to local decompilation fast.
    [SettingsEntry(5, "SourceLink fetch timeout (seconds)")]
    public int SourceLinkTimeoutSeconds;
}
