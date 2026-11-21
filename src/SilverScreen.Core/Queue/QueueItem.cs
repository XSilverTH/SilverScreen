using SilverScreen.Core.Domain;

namespace SilverScreen.Core.Queue;

public sealed record QueueItem(Guid Id, VideoSummary Video, DateTimeOffset AddedAt);