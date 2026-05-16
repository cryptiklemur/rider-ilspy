using JetBrains.Application.UI.Options;
using JetBrains.Application.UI.Options.OptionsDialog;
using JetBrains.IDE.UI.Options;
using JetBrains.Lifetimes;

namespace RiderIlSpy;

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
        AddHeader("General");
        AddBoolOption((IlSpySettings s) => s.Enabled, "Use ILSpy as the external decompiler", null);
        AddBoolOption((IlSpySettings s) => s.ShowDiagnosticBanner, "Show diagnostic banner at top of decompiled output", null);

        AddHeader("Output mode");
        AddComboEnum((IlSpySettings s) => s.OutputMode, "Default view:", mode => mode switch
        {
            IlSpyOutputMode.CSharp => "C# (decompiled)",
            IlSpyOutputMode.IL => "IL (disassembled)",
            IlSpyOutputMode.CSharpWithIL => "C# with IL",
            _ => mode.ToString(),
        });

        AddHeader("Runtime assembly lookup");
        AddCommentText("Extra directories ILSpy will search when an assembly cant be resolved (e.g. RimWorld_Data/Managed). Separate with ';' or ':'.");
        AddStringOption((IlSpySettings s) => s.AssemblyResolveDirs, "Extra search directories:", null, false);

        AddHeader("Decompiler output");
        AddBoolOption((IlSpySettings s) => s.AsyncAwait, "Reconstruct async/await", null);
        AddBoolOption((IlSpySettings s) => s.ExpressionBodies, "Use expression-bodied members for calculated getter-only properties", null);
        AddBoolOption((IlSpySettings s) => s.NamedArguments, "Use named arguments where helpful", null);
        AddBoolOption((IlSpySettings s) => s.ShowXmlDocumentation, "Show XML documentation comments", null);
        AddBoolOption((IlSpySettings s) => s.RemoveDeadCode, "Remove dead code", null);
        AddBoolOption((IlSpySettings s) => s.ThrowOnAssemblyResolveErrors, "Throw on assembly resolve errors (instead of best-effort output)", null);
        AddBoolOption((IlSpySettings s) => s.UsePrimaryConstructorSyntax, "Use primary constructor syntax with records (disable for go-to-definition correctness)", null);

        AddComboEnum((IlSpySettings s) => s.LanguageVersion, "Target C# language version:", v => v switch
        {
            IlSpyLanguageVersion.Latest => "Latest (default)",
            IlSpyLanguageVersion.CSharp11_0 => "C# 11.0",
            IlSpyLanguageVersion.CSharp10_0 => "C# 10.0",
            IlSpyLanguageVersion.CSharp9_0 => "C# 9.0",
            IlSpyLanguageVersion.CSharp8_0 => "C# 8.0",
            IlSpyLanguageVersion.CSharp7_3 => "C# 7.3",
            _ => v.ToString(),
        });
    }
}
