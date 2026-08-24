namespace SilverScreen.Core.Browsing.Common;

public sealed record VideoSummary(
    string Id,
    string Title,
    string ChannelName,
    TimeSpan Duration,
    string ThumbnailUrl,
    bool IsShort,
    string? WatchUrl = null,
    DateOnly? ApproximateUploadDate = null,
    DateTimeOffset? PublishedAt = null,
    string? ChannelUrl = null);