namespace SilverScreen.Core.Models;

public sealed record QueueItem(VideoSummary Video, DateTimeOffset QueuedAt);
