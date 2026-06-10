namespace RiderIlSpy;

/// <summary>
/// Pure decision for <see cref="IlSpyExternalSourcesProvider.IsApplicableForNavigation"/>.
/// Split out of the provider class so the contract is testable without
/// loading the JetBrains.ReSharper.Feature.Services assembly at test runtime
/// (the provider's field types pull in the whole IExternalSourcesProvider
/// graph, which isn't deployed to xUnit harness).
/// </summary>
public static class IlSpyNavigationApplicability
{
    /// <summary>
    /// Returns true when ILSpy should be advertised as a navigation target.
    ///
    /// Rules, in order:
    /// 1. When <paramref name="ignoreOptions"/> is true the platform is asking
    ///    "would you handle this if the user toggle were ignored?" — the
    ///    JetBrains convention is to answer affirmatively regardless of any
    ///    other gate so the platform can probe our capability honestly.
    /// 2. Otherwise the user toggle wins: applicable iff
    ///    <paramref name="ilSpyEnabled"/> is true.
    /// </summary>
    public static bool Decide(bool ilSpyEnabled, bool ignoreOptions) =>
        ignoreOptions || ilSpyEnabled;
}
