using System.Collections.ObjectModel;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Queue;

public sealed class QueueService : IQueueService
{
    private static readonly ILogger Logger = Log.ForContext<QueueService>();
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
        Logger.Information("Added video {VideoId} ({Title}) to playback queue", video.Id, video.Title);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
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
        var videoId = _items[index].Video.Id;
        _items.RemoveAt(index);
        Logger.Information("Removed video {VideoId} (QueueItemId: {QueueItemId}) from queue", videoId, itemId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;

        Logger.Information("Clearing all {Count} items from playback queue", _items.Count);
        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Replace(IEnumerable<VideoSummary> videos)
    {
        _items.Clear();
        foreach (var video in videos)
            _items.Add(new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now));
        Logger.Information("Replaced playback queue with {Count} items", _items.Count);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddNext(VideoSummary video)
    {
        var item = new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now);
        _items.Insert(0, item);
        Logger.Information("Enqueued video {VideoId} ({Title}) as next in playback queue", video.Id, video.Title);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}