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

namespace SilverScreen.Tests.Player.Comments;

public sealed class YtDlpCommentServiceTests
{
    private const string VideoId = "dQw4w9WgXcQ";



    [Fact]
    public async Task GetCommentsAsync_ParsesFiltersRelationshipsAndNormalizesCommentsInOrder()
    {
        var service = CreateService(new CapturingRunner(_ => Task.FromResult(Success("""
            {
              "comments": [
                { "id": "first", "parent": "root", "author": "", "text": "First text", "_time_text": "2 hours ago", "time_text": "ignored", "like_count": 7 },
                { "id": "first", "author": "Duplicate", "text": "Duplicate text", "like_count": 99 },
                { "id": " ", "author": "Skipped", "text": "No id" },
                { "id": "no-text", "author": "Skipped", "text": " " },
                { "id": "second", "parent": "first", "author": "Author", "text": "Second text", "time_text": "yesterday", "like_count": -1 },
                { "id": "third", "author": "Third", "text": "Third text", "like_count": "not a number" },
                { "id": "fourth", "author": "Fourth", "text": "Fourth text" }
              ]
            }
            """))));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.True(result.IsSuccess);
        Assert.Equal("Comments loaded.", result.StatusMessage);
        Assert.Equal(
            ["first", "second", "third", "fourth"],
            result.Comments.Select(comment => comment.Id));
        Assert.Equal(new YouTubeComment("first", "YouTube user", "First text", "2 hours ago", 7), result.Comments[0]);
        Assert.Equal(new YouTubeComment("second", "Author", "Second text", "yesterday", 0, "first"),
            result.Comments[1]);
        Assert.Equal(new YouTubeComment("third", "Third", "Third text", "", 0), result.Comments[2]);
        Assert.Equal(new YouTubeComment("fourth", "Fourth", "Fourth text", "", 0), result.Comments[3]);
    }


    private static YtDlpCommentService CreateService(CapturingRunner runner, ICookieFileProvider? cookies = null)
    {
        return new YtDlpCommentService(cookies ?? new FakeCookieFileProvider(() => null),
            new TestPreferences("comment-yt-dlp"), runner);
    }

    private static ProcessResult Success(string output)
    {
        return new ProcessResult(0, output, "");
    }
    private sealed class CapturingRunner(Func<ProcessStartInfo, Task<ProcessResult>> run) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return run(startInfo);
        }
    }

    private sealed class TestPreferences(string executablePath) : IPreferencesService
    {
        private readonly AppPreferences _preferences = new() { YtDlpExecutablePath = executablePath };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class FakeCookieFileProvider(Func<CookieFileLease?> create) : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile()
        {
            return create();
        }
    }
}
