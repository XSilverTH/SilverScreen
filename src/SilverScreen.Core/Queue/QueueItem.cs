using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Queue;

public sealed record QueueItem(Guid Id, VideoSummary Video, DateTimeOffset AddedAt);