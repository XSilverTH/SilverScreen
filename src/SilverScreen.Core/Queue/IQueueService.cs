using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Queue;

/// List-owner contract for the playback queue: the single mutable, ordered,
/// in-memory owner of play-next state. No persistence: the list lives only for
/// the process lifetime and is rebuilt by explicit
/// <c>Add</c>
/// /
/// <c>Replace</c>
/// calls.
/// Playback snapshots (
/// <c>PlaybackRequest</c>
/// ) are derived copies
/// (
/// <c>Videos</c>
/// list plus start index); sync is one-directional
/// queue-to-request only and a snapshot never writes back into the list.
/// Consumers holding a snapshot must start a new snapshot to observe later edits.
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