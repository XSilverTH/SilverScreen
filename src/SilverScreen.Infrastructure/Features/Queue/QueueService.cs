using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Queue;

public sealed class QueueService : IQueueService
{
    private readonly List<QueueItem> _items = [];

    public event EventHandler? Changed;

    public IReadOnlyList<QueueItem> Items => _items;

    public TimeSpan TotalDuration => TimeSpan.FromTicks(_items.Sum(item => item.Video.Duration.Ticks));

    public QueueItem Add(VideoSummary video)
    {
        var item = new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now);
        _items.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public void AddNext(VideoSummary video)
    {
        var item = new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now);
        _items.Insert(0, item);
        Changed?.Invoke(this, EventArgs.Empty);
        // return item;
    }

    public void Move(Guid itemId, int destinationIndex)
    {
        var currentIndex = _items.FindIndex(item => item.Id == itemId);
        if (currentIndex < 0 ||
            destinationIndex < 0 ||
            destinationIndex >= _items.Count ||
            currentIndex == destinationIndex)
            // return false;
            return;

        var item = _items[currentIndex];
        _items.RemoveAt(currentIndex);
        _items.Insert(destinationIndex, item);
        Changed?.Invoke(this, EventArgs.Empty);
        // return true;
    }

    public void Remove(Guid itemId)
    {
        var index = _items.FindIndex(item => item.Id == itemId);
        if (index < 0)
            // return false;
            return;
        _items.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        // return true;
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}