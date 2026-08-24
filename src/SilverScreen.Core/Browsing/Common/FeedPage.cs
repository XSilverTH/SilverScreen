using SilverScreen.Core.Browsing.Common;
namespace SilverScreen.Core.Browsing.Common;

public sealed record FeedPage(IReadOnlyList<VideoSummary> Videos, string? ContinuationToken = null)
{
    public static FeedPage Empty { get; } = new([]);
}