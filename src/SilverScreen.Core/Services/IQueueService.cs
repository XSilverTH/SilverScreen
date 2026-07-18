using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IQueueService
{
    IReadOnlyList<QueueItem> Items { get; }

    TimeSpan TotalDuration { get; }
    event EventHandler? Changed;

    QueueItem Add(VideoSummary video);

    QueueItem AddNext(VideoSummary video);

    bool Remove(QueueItem item);

    void Clear();
}