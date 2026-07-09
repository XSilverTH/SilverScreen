namespace SilverScreen.Core.Models;

public sealed record FeedPage(IReadOnlyList<VideoSummary> Videos, string? ContinuationToken = null)
{
    public static FeedPage Empty { get; } = new([]);
}
