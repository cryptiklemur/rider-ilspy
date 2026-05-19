using Xunit;

namespace RiderIlSpy.Tests;

// Pins the IsApplicableForNavigation contract. Rules:
//   1. ignoreOptions=true → always applicable (platform's "would you handle
//      this if options were ignored?" probe — answer honestly).
//   2. enabled=false → not applicable (user has the plugin switched off).
//   3. otherwise → applicable.
//
// The full IsApplicableForNavigation method requires a real
// IExternalSourcesProvider context to construct, so the gating logic lives
// in IlSpyNavigationApplicability.Decide — a pure static helper with no
// JetBrains dependencies — and is tested here directly.
public class IlSpyExternalSourcesProviderApplicabilityTests
{
    [Fact]
    public void IgnoreOptions_true_returns_true_even_when_disabled()
    {
        Assert.True(IlSpyNavigationApplicability.Decide(ilSpyEnabled: false, ignoreOptions: true));
    }

    [Fact]
    public void IgnoreOptions_true_returns_true_when_enabled()
    {
        Assert.True(IlSpyNavigationApplicability.Decide(ilSpyEnabled: true, ignoreOptions: true));
    }

    [Fact]
    public void IgnoreOptions_false_honors_user_toggle_when_enabled()
    {
        Assert.True(IlSpyNavigationApplicability.Decide(ilSpyEnabled: true, ignoreOptions: false));
    }

    [Fact]
    public void IgnoreOptions_false_returns_false_when_disabled()
    {
        Assert.False(IlSpyNavigationApplicability.Decide(ilSpyEnabled: false, ignoreOptions: false));
    }
}
