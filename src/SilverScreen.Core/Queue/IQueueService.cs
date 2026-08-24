using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Queue;

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