using System.Collections.ObjectModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Queue;

namespace SilverScreen.Infrastructure.Queue;

/// <summary>
///     List owner for the playback queue: the single mutable, ordered, in-memory
///     holder of play-next state. No persistence: entries live only for the process
///     lifetime and change only through <c>Add</c>/<c>Move</c>/<c>Remove</c>/<c>Clear</c>/<c>Replace</c>.
///     Sync is one-directional: edits flow from this service into an immutable
///     playback-request snapshot (videos plus the session's playlist index) published
///     through the playback session; the snapshot never writes back into the list.
///     The embedded view mirrors snapshots into the native playlist, never the reverse.
///     External-list callers receive derived watch URLs plus a start index, and an
///     empty list surfaces as a guidance status, never a throw (see
///     <c>PlaybackRequest.EmptyQueueMessage</c> and the coordinator entry guard).
/// </summary>
public sealed class QueueService : IQueueService
{
    private static readonly ILogger Logger = Log.ForContext<QueueService>();
    private readonly Lock _lock = new();
    private readonly List<QueueItem> _items = [];


    public event EventHandler? Changed;

    public IReadOnlyList<QueueItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }
    }

    public TimeSpan TotalDuration
    {
        get
        {
            lock (_lock)
            {
                var ticks = _items.Sum(item => item.Video.Duration.Ticks);
                return TimeSpan.FromTicks(ticks);
            }
        }
    }

    public QueueItem Add(VideoSummary video)
    {
        var item = new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now);
        lock (_lock)
        {
            _items.Add(item);
        }
        Logger.Information("Added video {VideoId} ({Title}) to playback queue", video.Id, video.Title);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public void Move(Guid itemId, int destinationIndex)
    {
        lock (_lock)
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
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid itemId)
    {
        string? videoId = null;
        lock (_lock)
        {
            var index = _items.FindIndex(item => item.Id == itemId);
            if (index < 0)
                return;
            videoId = _items[index].Video.Id;
            _items.RemoveAt(index);
        }
        Logger.Information("Removed video {VideoId} (QueueItemId: {QueueItemId}) from queue", videoId, itemId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        int count;
        lock (_lock)
        {
            count = _items.Count;
            if (count == 0)
                return;

            _items.Clear();
        }
        Logger.Information("Clearing all {Count} items from playback queue", count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Replace(IEnumerable<VideoSummary> videos)
    {
        int count;
        lock (_lock)
        {
            _items.Clear();
            foreach (var video in videos)
                _items.Add(new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now));
            count = _items.Count;
        }
        Logger.Information("Replaced playback queue with {Count} items", count);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}