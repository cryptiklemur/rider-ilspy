namespace RiderIlSpy.Search;

public sealed class IlSpyNavResolution
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; } = 1;
    public int Column { get; init; } = 1;
    public string ErrorMessage { get; init; } = string.Empty;

    public static IlSpyNavResolution Failure(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static IlSpyNavResolution Ok(string path, int line, int column) =>
        new() { Success = true, FilePath = path, Line = line, Column = column };
}
