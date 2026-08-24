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

using System.Diagnostics;

namespace SilverScreen.Tests.Browsing.Channel;

public sealed class YtDlpChannelServiceTests
{
    [Fact]
    public async Task GetChannelAsync_MapsMetadataVideosAndRequestedSort()
    {
        var runner = new CapturingRunner(new ProcessResult(0, """
                                                              {
                                                                "channel": "Example Channel",
                                                                "description": "Videos about examples.",
                                                                "thumbnail": "https://img.example/avatar.jpg",
                                                                "channel_follower_count": 1234,
                                                                "entries": [
                                                                  {
                                                                    "id": "dQw4w9WgXcQ",
                                                                    "title": "Example video",
                                                                    "channel": "Example Channel",
                                                                    "channel_url": "https://www.youtube.com/@example",
                                                                    "duration": 42
                                                                  }
                                                                ]
                                                              }
                                                              """, ""));
        var service = CreateService(runner);

        var page = await service.GetChannelAsync("https://www.youtube.com/@example", "Fallback",
            ChannelVideoSort.Popular, 1, CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal("Example Channel", page.Name);
        Assert.Equal("Videos about examples.", page.Description);
        Assert.Equal("https://img.example/avatar.jpg", page.AvatarUrl);
        Assert.Equal(1234, page.SubscriberCount);
        var video = Assert.Single(page.Videos);
        Assert.Equal("https://www.youtube.com/@example", video.ChannelUrl);
    }


    private static YtDlpChannelService CreateService(CapturingRunner runner)
    {
        return new YtDlpChannelService(new TestPreferences(), runner, new FakeCookieFileProvider());
    }

    private sealed class CapturingRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class TestPreferences : IPreferencesService
    {
        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return new AppPreferences { YtDlpExecutablePath = "yt-dlp-test", MaxResults = 25 };
        }

        public void SavePreferences(AppPreferences preferences)
        {
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class FakeCookieFileProvider : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile()
        {
            return null;
        }
    }
}
