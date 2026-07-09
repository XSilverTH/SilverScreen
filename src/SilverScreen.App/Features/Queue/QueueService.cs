using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Queue;

public sealed class QueueService : IQueueService
{
    private readonly List<QueueItem> _items = [];

    public event EventHandler? Changed;

    public IReadOnlyList<QueueItem> Items => _items;

    public TimeSpan TotalDuration => TimeSpan.FromTicks(_items.Sum(item => item.Video.Duration.Ticks));

    public QueueItem Add(VideoSummary video)
    {
        var item = new QueueItem(video, DateTimeOffset.Now, _items.Count);
        _items.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public QueueItem AddNext(VideoSummary video)
    {
        var item = new QueueItem(video, DateTimeOffset.Now, 0);
        _items.Insert(0, item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public bool Remove(QueueItem item)
    {
        if (_items.Remove(item))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
