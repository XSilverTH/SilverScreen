namespace SilverScreen.Core.Browsing.Common;

public sealed record YouTubeVideoDetails(
    string? Description,
    long? ViewCount,
    DateTimeOffset? PublishedAt,
    string Title,
    string ChannelName);