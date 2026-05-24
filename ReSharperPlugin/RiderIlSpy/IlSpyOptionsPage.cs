using JetBrains.Application.UI.Options;
using JetBrains.Application.UI.Options.OptionsDialog;
using JetBrains.IDE.UI.Options;
using JetBrains.Lifetimes;
using RiderIlSpy.Resources;

namespace RiderIlSpy;

// The [OptionsPage] display-name argument must be a compile-time const, so the
// English literal stays inline here; the matching localized string lives at
// Strings.OptionsPage_DisplayName for any future custom-renderer path. Keep
// the two in sync if either side changes.
[OptionsPage(Pid, "ILSpy Decompiler", null)]
public class IlSpyOptionsPage : BeSimpleOptionsPage
{
    // Framework-imposed triplication — keep in sync with PAGE_ID in
    // src/main/kotlin/com/cryptiklemur/riderilspy/IlSpyOptionsPage.kt (canonical source)
    // and the <applicationConfigurable id=...> attribute in plugin.xml.
    public const string Pid = "RiderIlSpyOptionsPage";

    public IlSpyOptionsPage(Lifetime lifetime, OptionsPageContext optionsPageContext, OptionsSettingsSmartContext optionsSettingsSmartContext)
        : base(lifetime, optionsPageContext, optionsSettingsSmartContext)
    {
        AddHeader(Strings.OptionsPage_Header_General);
        AddBoolOption((IlSpySettings s) => s.Enabled, Strings.OptionsPage_Enabled_Label, null);
        AddBoolOption((IlSpySettings s) => s.ShowDiagnosticBanner, Strings.OptionsPage_ShowDiagnosticBanner_Label, null);

        AddHeader(Strings.OptionsPage_Header_OutputMode);
        AddComboEnum((IlSpySettings s) => s.OutputMode, Strings.OptionsPage_OutputMode_Label, mode => mode switch
        {
            IlSpyOutputMode.CSharp => Strings.OptionsPage_OutputMode_CSharp,
            IlSpyOutputMode.IL => Strings.OptionsPage_OutputMode_IL,
            IlSpyOutputMode.CSharpWithIL => Strings.OptionsPage_OutputMode_CSharpWithIL,
            _ => mode.ToString(),
        });

        AddHeader(Strings.OptionsPage_Header_RuntimeAssemblyLookup);
        AddCommentText(Strings.OptionsPage_AssemblyResolveDirs_Comment);
        AddStringOption((IlSpySettings s) => s.AssemblyResolveDirs, Strings.OptionsPage_AssemblyResolveDirs_Label, null, false);

        AddHeader(Strings.OptionsPage_Header_DecompilerOutput);
        AddBoolOption((IlSpySettings s) => s.AsyncAwait, Strings.OptionsPage_AsyncAwait_Label, null);
        AddBoolOption((IlSpySettings s) => s.ExpressionBodies, Strings.OptionsPage_ExpressionBodies_Label, null);
        AddBoolOption((IlSpySettings s) => s.NamedArguments, Strings.OptionsPage_NamedArguments_Label, null);
        AddBoolOption((IlSpySettings s) => s.ShowXmlDocumentation, Strings.OptionsPage_ShowXmlDocumentation_Label, null);
        AddBoolOption((IlSpySettings s) => s.RemoveDeadCode, Strings.OptionsPage_RemoveDeadCode_Label, null);
        AddBoolOption((IlSpySettings s) => s.ThrowOnAssemblyResolveErrors, Strings.OptionsPage_ThrowOnAssemblyResolveErrors_Label, null);
        AddBoolOption((IlSpySettings s) => s.UsePrimaryConstructorSyntax, Strings.OptionsPage_UsePrimaryConstructorSyntax_Label, null);

        AddComboEnum((IlSpySettings s) => s.LanguageVersion, Strings.OptionsPage_LanguageVersion_Label, v => v switch
        {
            IlSpyLanguageVersion.Latest => Strings.OptionsPage_LanguageVersion_Latest,
            IlSpyLanguageVersion.CSharp11_0 => Strings.OptionsPage_LanguageVersion_CSharp11_0,
            IlSpyLanguageVersion.CSharp10_0 => Strings.OptionsPage_LanguageVersion_CSharp10_0,
            IlSpyLanguageVersion.CSharp9_0 => Strings.OptionsPage_LanguageVersion_CSharp9_0,
            IlSpyLanguageVersion.CSharp8_0 => Strings.OptionsPage_LanguageVersion_CSharp8_0,
            IlSpyLanguageVersion.CSharp7_3 => Strings.OptionsPage_LanguageVersion_CSharp7_3,
            _ => v.ToString(),
        });

        AddHeader(Strings.OptionsPage_Header_ReferenceAssemblies);
        AddCommentText(Strings.OptionsPage_ReferenceAssemblies_Comment);
        AddBoolOption((IlSpySettings s) => s.DecompileReferenceAssemblies, Strings.OptionsPage_DecompileReferenceAssemblies_Label, null);

        AddHeader(Strings.OptionsPage_Header_SourceLink);
        AddCommentText(Strings.OptionsPage_SourceLink_Comment);
        AddBoolOption((IlSpySettings s) => s.PreferSourceLink, Strings.OptionsPage_PreferSourceLink_Label, null);
        AddIntOption((IlSpySettings s) => s.SourceLinkTimeoutSeconds, Strings.OptionsPage_SourceLinkTimeoutSeconds_Label);
    }
}
