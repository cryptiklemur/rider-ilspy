namespace RiderIlSpy;

/// <summary>
/// SDK-free mirror of the ICSharpCode.Decompiler settings the plugin exposes to
/// users. The host builds this from the live settings store; the engine converts
/// it into a real <c>DecompilerSettings</c> on its side of the AssemblyLoadContext
/// boundary. Carrying the plain record across (instead of DecompilerSettings
/// itself) is what keeps the host assembly free of ICSharpCode types.
/// </summary>
public sealed record IlSpyDecompilerOptions(
    bool ThrowOnAssemblyResolveErrors,
    bool AsyncAwait,
    bool ExpressionBodies,
    bool NamedArguments,
    bool ShowXmlDocumentation,
    bool RemoveDeadCode,
    bool UsePrimaryConstructorSyntax,
    IlSpyLanguageVersion LanguageVersion);
