using Xunit;

namespace RiderIlSpy.Tests;

// Pins the IsPreferredForNavigation contract: ILSpy is the preferred
// provider only when (a) the user has the plugin enabled AND (b) the user
// has NOT opted to defer to Rider's downloaded sources. The full
// IsPreferredForNavigation method requires a real IExternalSourcesProvider
// context to construct, so the gating logic lives in
// IlSpyNavigationPreference.Decide — a pure static helper with no JetBrains
// dependencies — and is tested here directly.
//
// Regression context: before DeferToRiderSources existed, ILSpy always
// returned IsPreferredForNavigation=true (when enabled), short-circuiting
// peer providers including Rider's downloaded-source path. That caused
// BCL navigation to always decompile instead of opening real source.
// These tests pin the new behavior so the regression cannot return.
public class IlSpyExternalSourcesProviderPreferenceTests
{
    [Fact]
    public void Disabled_is_never_preferred()
    {
        Assert.False(IlSpyNavigationPreference.Decide(ilSpyEnabled: false, deferToRiderSources: false));
    }

    [Fact]
    public void Disabled_and_deferring_is_never_preferred()
    {
        Assert.False(IlSpyNavigationPreference.Decide(ilSpyEnabled: false, deferToRiderSources: true));
    }

    [Fact]
    public void Enabled_and_not_deferring_is_preferred()
    {
        Assert.True(IlSpyNavigationPreference.Decide(ilSpyEnabled: true, deferToRiderSources: false));
    }

    [Fact]
    public void Enabled_but_deferring_is_not_preferred()
    {
        Assert.False(IlSpyNavigationPreference.Decide(ilSpyEnabled: true, deferToRiderSources: true));
    }
}
