using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IQueueService
{
    event EventHandler? Changed;

    IReadOnlyList<QueueItem> Items { get; }

    TimeSpan TotalDuration { get; }

    QueueItem Add(VideoSummary video);

    QueueItem AddNext(VideoSummary video);

    bool Remove(QueueItem item);

    void Clear();
}