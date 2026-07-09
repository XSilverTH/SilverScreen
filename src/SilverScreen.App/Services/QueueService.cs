using SilverScreen.Core.Models;

namespace SilverScreen.Services;

public sealed class QueueService
{
    private readonly List<QueueItem> _items = [];

    public event EventHandler? Changed;

    public IReadOnlyList<QueueItem> Items => _items;

    public TimeSpan TotalDuration => TimeSpan.FromTicks(_items.Sum(item => item.Video.Duration.Ticks));

    public void Add(VideoSummary video)
    {
        _items.Add(new QueueItem(video, DateTimeOffset.Now));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddNext(VideoSummary video)
    {
        _items.Insert(0, new QueueItem(video, DateTimeOffset.Now));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(QueueItem item)
    {
        if (_items.Remove(item))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
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
