using System;
using System.Collections.Generic;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using JetBrains.Application.Settings;
using JetBrains.Util;

namespace RiderIlSpy;

/// <summary>
/// SDK-bound builder that translates the live <see cref="IlSpySettings"/> bag
/// into a fully-populated <see cref="IlSpyRequestSettings"/> snapshot. Owned
/// jointly by <see cref="IlSpyExternalSourcesProvider"/> (navigation path) and
/// <see cref="SaveAsProjectProtocolHandler"/> (save-as-project path) so both
/// entry points read settings through one canonical seam — eliminates the
/// two-sources-of-truth problem the reviewer flagged when these reads were
/// duplicated across the provider and the protocol handler.
/// </summary>
/// <remarks>
/// Construction is cheap (just stashes the bound-live store and logger). The
/// real work happens in <see cref="Snapshot"/>, called once at the top of each
/// request path. Lives separately from
/// <see cref="IlSpyExternalSourcesProviderHelpers"/> because that file is
/// intentionally SDK-free for testability — adding the
/// <see cref="IContextBoundSettingsStoreLive"/> dependency here would defeat
/// that isolation.
/// </remarks>
public sealed class IlSpyRequestSettingsBuilder
{
    private readonly IContextBoundSettingsStoreLive mySettings;
    private readonly ILogger myLogger;

    public IlSpyRequestSettingsBuilder(IContextBoundSettingsStoreLive settings, ILogger logger)
    {
        mySettings = settings;
        myLogger = logger;
    }

    /// <summary>
    /// Captures every settings value driving one decompile pass into an
    /// immutable <see cref="IlSpyRequestSettings"/>. <paramref name="mode"/> is
    /// the externally-resolved output mode — the navigation path passes the
    /// rd-live-or-persisted result, the save-as-project path passes a constant
    /// (SaveAsProject is C# only). Threading the mode in rather than reading
    /// it here keeps the rd-vs-settings policy at the call site where the
    /// surrounding context makes it obvious.
    /// </summary>
    public IlSpyRequestSettings Snapshot(IlSpyOutputMode mode)
    {
        return new IlSpyRequestSettings(
            Mode: mode,
            DecompilerSettings: BuildDecompilerSettings(),
            ExtraSearchDirs: GetExtraSearchDirs(),
            ShowBanner: mySettings.GetValue((IlSpySettings s) => s.ShowDiagnosticBanner),
            PreferSourceLink: mySettings.GetValue((IlSpySettings s) => s.PreferSourceLink),
            SourceLinkTimeoutSeconds: mySettings.GetValue((IlSpySettings s) => s.SourceLinkTimeoutSeconds));
    }

    private DecompilerSettings BuildDecompilerSettings()
    {
        DecompilerSettings settings = new DecompilerSettings
        {
            ThrowOnAssemblyResolveErrors = mySettings.GetValue((IlSpySettings s) => s.ThrowOnAssemblyResolveErrors),
            AsyncAwait = mySettings.GetValue((IlSpySettings s) => s.AsyncAwait),
            UseExpressionBodyForCalculatedGetterOnlyProperties = mySettings.GetValue((IlSpySettings s) => s.ExpressionBodies),
            NamedArguments = mySettings.GetValue((IlSpySettings s) => s.NamedArguments),
            ShowXmlDocumentation = mySettings.GetValue((IlSpySettings s) => s.ShowXmlDocumentation),
            RemoveDeadCode = mySettings.GetValue((IlSpySettings s) => s.RemoveDeadCode),
            UsePrimaryConstructorSyntax = mySettings.GetValue((IlSpySettings s) => s.UsePrimaryConstructorSyntax),
        };
        // Apply language-version downgrade after construction so unspecified
        // (Latest) leaves ILSpy's defaults untouched. SetLanguageVersion flips
        // multiple feature flags (RecordClasses, InitAccessors, ...) to match
        // the target version's capability set.
        IlSpyLanguageVersion languageVersion = mySettings.GetValue((IlSpySettings s) => s.LanguageVersion);
        if (languageVersion != IlSpyLanguageVersion.Latest)
        {
            settings.SetLanguageVersion((LanguageVersion)(int)languageVersion);
        }
        return settings;
    }

    private IReadOnlyList<string> GetExtraSearchDirs()
    {
        string raw = mySettings.GetValue((IlSpySettings s) => s.AssemblyResolveDirs) ?? "";
        if (raw.Length == 0) return Array.Empty<string>();
        string[] parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        List<string> result = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (IlSpyExternalSourcesProviderHelpers.TryNormalizeSearchDir(part, out string canonical, out string? rejection))
            {
                result.Add(canonical);
            }
            else if (rejection != null)
            {
                myLogger.Warn(rejection);
            }
        }
        return result;
    }
}
