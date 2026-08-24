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

namespace SilverScreen.Tests.Browsing.Search;

public sealed class YtDlpSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_MapsSuccessfulYtDlpResults()
    {
        var service = CreateService("""
                                    { "entries": [
                                      { "id": "dQw4w9WgXcQ", "title": "Video", "uploader": "Channel", "duration": 213 }
                                    ] }
                                    """);

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("dQw4w9WgXcQ", video.Id);
        Assert.Equal("Video", video.Title);
        Assert.Equal(TimeSpan.FromSeconds(213), video.Duration);
    }

    [Fact]
    public async Task SearchAsync_FiltersDetectableShorts()
    {
        var service = CreateService("""
                                    { "entries": [
                                      { "id": "shortVideo1", "title": "Short", "webpage_url": "https://youtube.com/shorts/shortVideo1" },
                                      { "id": "normalVid01", "title": "Normal" }
                                    ] }
                                    """);

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.Equal(["normalVid01"], result.Videos.Select(video => video.Id));
    }


    private static YtDlpSearchService CreateService(string output, int exitCode = 0, string standardError = "")
    {
        return new YtDlpSearchService(
            new TestPreferences(),
            new FakeRunner(new ProcessResult(exitCode, output, standardError)));
    }

    private sealed class TestPreferences : IPreferencesService
    {
        public event EventHandler<AppPreferences>? PreferencesChanged
        {
            add { }
            remove { }
        }

        public AppPreferences GetPreferences()
        {
            return new AppPreferences();
        }

        public void SavePreferences(AppPreferences preferences)
        {
        }
    }

    private sealed class FakeRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}
