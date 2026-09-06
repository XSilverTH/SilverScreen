using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Queue;

/// <summary>
///     List-owner contract for the playback queue. Implementations own the mutable
///     item list; playback snapshots (<c>PlaybackRequest</c>) are derived from it and
///     sync flows queue-to-request only, never back.
/// </summary>
public interface IQueueService
{
    IReadOnlyList<QueueItem> Items { get; }

    TimeSpan TotalDuration { get; }
    event EventHandler? Changed;

    QueueItem Add(VideoSummary video);

    void Move(Guid itemId, int destinationIndex);

    void Remove(Guid itemId);

    void Clear();

    void Replace(IEnumerable<VideoSummary> videos);
}