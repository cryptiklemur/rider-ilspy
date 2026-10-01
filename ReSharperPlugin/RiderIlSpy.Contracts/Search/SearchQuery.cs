namespace RiderIlSpy.Search;

public static class SearchQuery
{
    /// <summary>False for input that would match every candidate in the corpus.</summary>
    public static bool IsRunnable(string? input) => !string.IsNullOrWhiteSpace(input);
}
