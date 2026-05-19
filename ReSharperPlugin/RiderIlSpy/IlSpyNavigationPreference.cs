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
    /// navigation target, short-circuiting peer providers. When
    /// <paramref name="deferToRiderSources"/> is true, returns false so
    /// Rider's own preferred-source providers (downloaded source,
    /// SourceLink, Microsoft Reference Source) get to claim the navigation
    /// for types where they can produce real source. ILSpy remains the
    /// fallback via <see cref="IlSpyExternalSourcesProvider.IsApplicableForNavigation"/>
    /// when no other provider claims the navigation.
    ///
    /// When <paramref name="ilSpyEnabled"/> is false, returns false
    /// unconditionally (the user has the plugin switched off).
    /// </summary>
    public static bool Decide(bool ilSpyEnabled, bool deferToRiderSources) =>
        ilSpyEnabled && !deferToRiderSources;
}
