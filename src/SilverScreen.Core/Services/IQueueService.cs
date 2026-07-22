using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IQueueService
{
    IReadOnlyList<QueueItem> Items { get; }

    TimeSpan TotalDuration { get; }
    event EventHandler? Changed;

    QueueItem Add(VideoSummary video);

    void AddNext(VideoSummary video);

    void Move(Guid itemId, int destinationIndex);

    void Remove(Guid itemId);

    void Clear();
}