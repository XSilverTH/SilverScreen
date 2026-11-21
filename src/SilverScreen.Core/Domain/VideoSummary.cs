namespace SilverScreen.Core.Domain;

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
    string? ChannelUrl = null,
    YouTubePlaybackProgress? PlaybackProgress = null);