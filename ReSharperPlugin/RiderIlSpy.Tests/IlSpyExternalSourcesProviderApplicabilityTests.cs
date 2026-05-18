using Xunit;

namespace RiderIlSpy.Tests;

// Pins the IsApplicableForNavigation contract: when ignoreOptions=true the
// provider must advertise availability regardless of the user toggle (the
// JetBrains platform convention for "would you handle this if options were
// ignored?"); when ignoreOptions=false the user's IlSpySettings.Enabled gates
// it. The full IsApplicableForNavigation method requires a real
// IExternalSourcesProvider context to construct, so the gating logic is
// extracted into IlSpyNavigationApplicability.Decide — a pure static helper
// with no JetBrains dependencies — and tested here directly.
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
