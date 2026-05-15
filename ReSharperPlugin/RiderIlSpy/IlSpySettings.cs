using JetBrains.Application.Settings;
using JetBrains.Application.Settings.WellKnownRootKeys;

namespace RiderIlSpy;

public enum IlSpyOutputMode
{
    CSharp = 0,
    IL = 1,
    CSharpWithIL = 2,
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

    [SettingsEntry("", "Extra directories to search for runtime assemblies, separated by ';' (Linux/macOS) or ':' (any)")]
    public string AssemblyResolveDirs = "";

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
}
