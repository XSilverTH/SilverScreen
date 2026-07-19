namespace SilverScreen.Core.Models;

public sealed record QueueItem(Guid Id, VideoSummary Video, DateTimeOffset AddedAt);