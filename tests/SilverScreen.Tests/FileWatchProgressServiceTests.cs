using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class FileWatchProgressServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"silverscreen-watch-progress-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Fact]
    public void Update_PersistsAndPublishesHighestPartialProgress()
    {
        var path = Path.Combine(_directory, "watch-progress.json");
        var service = new FileWatchProgressService(path);
        WatchProgress? changed = null;
        service.ProgressChanged += (_, progress) => changed = progress;

        service.Update(Request(), State(25));
        service.Update(Request(), State(10));

        Assert.Equal(0.25, service.GetFraction("abc123_X-yZ"));
        Assert.Equal(new WatchProgress("abc123_X-yZ", 0.25), changed);
        Assert.Equal(0.25, new FileWatchProgressService(path).GetFraction("abc123_X-yZ"));
    }

    [Fact]
    public void Update_NearCompletionMarksVideoFullyWatched()
    {
        var service = new FileWatchProgressService(Path.Combine(_directory, "watch-progress.json"));

        service.Update(Request(), State(91));

        Assert.Equal(1, service.GetFraction("abc123_X-yZ"));
    }

    [Fact]
    public void Update_AtTheStartDoesNotCreateVisibleProgress()
    {
        var service = new FileWatchProgressService(Path.Combine(_directory, "watch-progress.json"));

        service.Update(Request(), State(1));

        Assert.Null(service.GetFraction("abc123_X-yZ"));
    }

    private static PlaybackRequest Request()
    {
        return new PlaybackRequest([
            new VideoSummary("abc123_X-yZ", "Video", "Channel", TimeSpan.FromSeconds(100), "", false)
        ]);
    }

    private static PlaybackPresenceState State(double positionSeconds)
    {
        return new PlaybackPresenceState(0, TimeSpan.FromSeconds(positionSeconds), TimeSpan.FromSeconds(100), false, 1,
            DateTimeOffset.UtcNow);
    }
}
