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
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


namespace SilverScreen.Tests.Player;

public sealed class FileWatchProgressServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"silverscreen-watch-progress-{Guid.NewGuid():N}");

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
        Assert.Equal(0.10, service.GetResumeFraction("abc123_X-yZ"));
        Assert.Equal(new WatchProgress("abc123_X-yZ", 0.25), changed);
        Assert.Equal(0.25, new FileWatchProgressService(path).GetFraction("abc123_X-yZ"));
        Assert.Equal(0.10, new FileWatchProgressService(path).GetResumeFraction("abc123_X-yZ"));
    }

    [Fact]
    public void Update_NearCompletionMarksVideoFullyWatched()
    {
        var service = new FileWatchProgressService(Path.Combine(_directory, "watch-progress.json"));

        service.Update(Request(), State(91));

        Assert.Equal(1, service.GetFraction("abc123_X-yZ"));
        Assert.Null(service.GetResumeFraction("abc123_X-yZ"));
    }

    [Fact]
    public void Load_LegacyProgressProvidesCardAndResumeFractions()
    {
        var path = Path.Combine(_directory, "watch-progress.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"abc123_X-yZ":0.4}""");

        var service = new FileWatchProgressService(path);

        Assert.Equal(0.4, service.GetFraction("abc123_X-yZ"));
        Assert.Equal(0.4, service.GetResumeFraction("abc123_X-yZ"));
    }

    [Fact]
    public void Update_WhenRewoundToTheStart_ClearsResumeButRetainsCardProgress()
    {
        var service = new FileWatchProgressService(Path.Combine(_directory, "watch-progress.json"));
        service.Update(Request(), State(25));

        service.Update(Request(), State(1));

        Assert.Equal(0.25, service.GetFraction("abc123_X-yZ"));
        Assert.Null(service.GetResumeFraction("abc123_X-yZ"));
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
