using System.Globalization;
using System.Resources;

namespace RiderIlSpy.Resources;

// Static accessor over Strings.resx. We hand-roll this instead of relying on
// resx -> Designer.cs codegen so:
//   1) The keyspace is grep-able from source (each property is a literal),
//   2) The build doesn't depend on the resx-to-cs MSBuild target being wired,
//   3) Adding a new locale only requires dropping Strings.<lang>.resx next to
//      the baseline file -- ResourceManager resolves the cultured overlay
//      automatically.
//
// Missing keys fall back to the key name so a typo is loud instead of silently
// returning "". Format-style keys delegate to string.Format with current UI culture.
internal static class Strings
{
    private static readonly ResourceManager ResourceManager = new(
        "RiderIlSpy.Resources.Strings",
        typeof(Strings).Assembly);

    private static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);

    public static string OptionsPage_DisplayName => Get(nameof(OptionsPage_DisplayName));

    public static string OptionsPage_Header_General => Get(nameof(OptionsPage_Header_General));
    public static string OptionsPage_Enabled_Label => Get(nameof(OptionsPage_Enabled_Label));
    public static string OptionsPage_ShowDiagnosticBanner_Label => Get(nameof(OptionsPage_ShowDiagnosticBanner_Label));

    public static string OptionsPage_Header_OutputMode => Get(nameof(OptionsPage_Header_OutputMode));
    public static string OptionsPage_OutputMode_Label => Get(nameof(OptionsPage_OutputMode_Label));
    public static string OptionsPage_OutputMode_CSharp => Get(nameof(OptionsPage_OutputMode_CSharp));
    public static string OptionsPage_OutputMode_IL => Get(nameof(OptionsPage_OutputMode_IL));
    public static string OptionsPage_OutputMode_CSharpWithIL => Get(nameof(OptionsPage_OutputMode_CSharpWithIL));

    public static string OptionsPage_Header_RuntimeAssemblyLookup => Get(nameof(OptionsPage_Header_RuntimeAssemblyLookup));
    public static string OptionsPage_AssemblyResolveDirs_Comment => Get(nameof(OptionsPage_AssemblyResolveDirs_Comment));
    public static string OptionsPage_AssemblyResolveDirs_Label => Get(nameof(OptionsPage_AssemblyResolveDirs_Label));

    public static string OptionsPage_Header_DecompilerOutput => Get(nameof(OptionsPage_Header_DecompilerOutput));
    public static string OptionsPage_AsyncAwait_Label => Get(nameof(OptionsPage_AsyncAwait_Label));
    public static string OptionsPage_ExpressionBodies_Label => Get(nameof(OptionsPage_ExpressionBodies_Label));
    public static string OptionsPage_NamedArguments_Label => Get(nameof(OptionsPage_NamedArguments_Label));
    public static string OptionsPage_ShowXmlDocumentation_Label => Get(nameof(OptionsPage_ShowXmlDocumentation_Label));
    public static string OptionsPage_RemoveDeadCode_Label => Get(nameof(OptionsPage_RemoveDeadCode_Label));
    public static string OptionsPage_ThrowOnAssemblyResolveErrors_Label => Get(nameof(OptionsPage_ThrowOnAssemblyResolveErrors_Label));
    public static string OptionsPage_UsePrimaryConstructorSyntax_Label => Get(nameof(OptionsPage_UsePrimaryConstructorSyntax_Label));

    public static string OptionsPage_LanguageVersion_Label => Get(nameof(OptionsPage_LanguageVersion_Label));
    public static string OptionsPage_LanguageVersion_Latest => Get(nameof(OptionsPage_LanguageVersion_Latest));
    public static string OptionsPage_LanguageVersion_CSharp11_0 => Get(nameof(OptionsPage_LanguageVersion_CSharp11_0));
    public static string OptionsPage_LanguageVersion_CSharp10_0 => Get(nameof(OptionsPage_LanguageVersion_CSharp10_0));
    public static string OptionsPage_LanguageVersion_CSharp9_0 => Get(nameof(OptionsPage_LanguageVersion_CSharp9_0));
    public static string OptionsPage_LanguageVersion_CSharp8_0 => Get(nameof(OptionsPage_LanguageVersion_CSharp8_0));
    public static string OptionsPage_LanguageVersion_CSharp7_3 => Get(nameof(OptionsPage_LanguageVersion_CSharp7_3));

    public static string OptionsPage_Header_ReferenceAssemblies => Get(nameof(OptionsPage_Header_ReferenceAssemblies));
    public static string OptionsPage_ReferenceAssemblies_Comment => Get(nameof(OptionsPage_ReferenceAssemblies_Comment));
    public static string OptionsPage_DecompileReferenceAssemblies_Label => Get(nameof(OptionsPage_DecompileReferenceAssemblies_Label));

    public static string OptionsPage_Header_SourceLink => Get(nameof(OptionsPage_Header_SourceLink));
    public static string OptionsPage_SourceLink_Comment => Get(nameof(OptionsPage_SourceLink_Comment));
    public static string OptionsPage_PreferSourceLink_Label => Get(nameof(OptionsPage_PreferSourceLink_Label));
    public static string OptionsPage_SourceLinkTimeoutSeconds_Label => Get(nameof(OptionsPage_SourceLinkTimeoutSeconds_Label));

    public static string NavResolver_TypeDefinitionNotFound => Get(nameof(NavResolver_TypeDefinitionNotFound));

    public static string Search_IndexNotReady => Get(nameof(Search_IndexNotReady));
    public static string Search_InvalidTokenFormat => Get(nameof(Search_InvalidTokenFormat));
    public static string Search_UnknownQueryType(string queryType) =>
        Format(nameof(Search_UnknownQueryType), queryType);
}
