namespace RiderIlSpy;

/// <summary>
/// Pure decision for <see cref="IlSpyExternalSourcesProvider.IsPreferredForNavigation"/>.
/// Split out for the same reason as <see cref="IlSpyNavigationApplicability"/>:
/// keeps the gating contract unit-testable without loading the
/// JetBrains.ReSharper.Feature.Services assembly at test runtime.
/// </summary>
public static class IlSpyNavigationPreference
{
    /// <summary>
    /// Returns true when ILSpy should be advertised as the *preferred*
    /// navigation target, jumping to the front of the platform's provider
    /// iteration order.
    ///
    /// Why this is unconditional when enabled: the orchestrator
    /// (ExternalSourcesServiceImpl.NavigateToSources in
    /// JetBrains.ReSharper.Feature.Services.ExternalSources.dll) picks the
    /// first applicable provider whose NavigateToSources returns a non-empty
    /// result. When no provider is preferred, providers run in registration
    /// order — and JB's bundled DecompiledSourcesExternalSourcesProvider
    /// registers before this plugin's, so it wins every race and the user
    /// sees JB's decompiled output instead of ILSpy's. Returning true here
    /// puts ILSpy at the front of the queue, restoring the user's expected
    /// behavior ("ILSpy is on, so ILSpy decompiles").
    ///
    /// Real-source delivery for SourceLink-backed types is handled inside
    /// our own NavigateToSources, which attempts SourceLink first before
    /// falling back to ILSpy decompilation — so being preferred does NOT
    /// rob users of real source for libraries that publish SourceLink.
    ///
    /// When <paramref name="ilSpyEnabled"/> is false, returns false
    /// unconditionally (the user has the plugin switched off via the
    /// status-bar widget).
    /// </summary>
    public static bool Decide(bool ilSpyEnabled) => ilSpyEnabled;
}
