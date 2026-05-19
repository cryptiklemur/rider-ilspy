using Xunit;

namespace RiderIlSpy.Tests;

// Pins the IsPreferredForNavigation contract: ILSpy is the preferred
// provider whenever the plugin is enabled. The full IsPreferredForNavigation
// method requires a real IExternalSourcesProvider context to construct, so
// the gating logic lives in IlSpyNavigationPreference.Decide — a pure
// static helper with no JetBrains dependencies — and is tested here
// directly.
//
// Regression context (do not "fix" by re-introducing a defer flag):
// JetBrains' bundled DecompiledSourcesExternalSourcesProvider is also
// applicable for the same compiled-element navigations as this plugin.
// The orchestrator (ExternalSourcesServiceImpl.NavigateToSources) iterates
// applicable providers in registration order and picks the first one whose
// NavigateToSources returns non-empty. Without IsPreferred=true, JB's
// provider — registered before this plugin — always wins, so the user
// sees JB's decompiled output even with ILSpy enabled. A prior change
// returned IsPreferred=false hoping the platform would fall through to
// Rider's downloaded-source / SourceLink path; in practice it just handed
// the win to JB's decompiler. These tests pin the corrected behavior.
public class IlSpyExternalSourcesProviderPreferenceTests
{
    [Fact]
    public void Disabled_is_never_preferred()
    {
        Assert.False(IlSpyNavigationPreference.Decide(ilSpyEnabled: false));
    }

    [Fact]
    public void Enabled_is_preferred()
    {
        Assert.True(IlSpyNavigationPreference.Decide(ilSpyEnabled: true));
    }
}
