using System.Collections.ObjectModel;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Queue;

public sealed class QueueService : IQueueService
{
    private readonly List<QueueItem> _items = [];
    private readonly ReadOnlyCollection<QueueItem> _readOnlyItems;

    public QueueService()
    {
        _readOnlyItems = _items.AsReadOnly();
    }


    public event EventHandler? Changed;

    public IReadOnlyList<QueueItem> Items => _readOnlyItems;

    public TimeSpan TotalDuration
    {
        get
        {
            var ticks = _items.Sum(item => item.Video.Duration.Ticks);
            return TimeSpan.FromTicks(ticks);
        }
    }

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
    }

    public void Move(Guid itemId, int destinationIndex)
    {
        var currentIndex = _items.FindIndex(item => item.Id == itemId);
        if (currentIndex < 0 ||
            destinationIndex < 0 ||
            destinationIndex >= _items.Count ||
            currentIndex == destinationIndex)
            return;

        var item = _items[currentIndex];
        _items.RemoveAt(currentIndex);
        _items.Insert(destinationIndex, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid itemId)
    {
        var index = _items.FindIndex(item => item.Id == itemId);
        if (index < 0)
            return;
        _items.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}