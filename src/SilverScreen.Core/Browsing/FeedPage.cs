using SilverScreen.Core.Domain;

namespace SilverScreen.Core.Browsing;

public sealed record FeedPage(IReadOnlyList<VideoSummary> Videos, string? ContinuationToken = null)
{
    public static FeedPage Empty { get; } = new([]);
}