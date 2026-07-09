namespace SilverScreen.Core.Models;

public sealed record QueueItem(VideoSummary Video, DateTimeOffset AddedAt, int Position = 0);
