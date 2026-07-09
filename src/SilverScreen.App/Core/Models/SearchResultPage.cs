namespace SilverScreen.Core.Models;

public sealed record SearchResultPage(
    IReadOnlyList<VideoSummary> Videos,
    string? StatusMessage = null,
    bool IsSuccess = true,
    string? ContinuationToken = null)
{
    public static SearchResultPage Empty { get; } = new([], "No results found.");

    public static SearchResultPage Failed(string message) => new([], message, false);
}
