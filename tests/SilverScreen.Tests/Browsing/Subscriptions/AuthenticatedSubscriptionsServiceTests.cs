using System.Diagnostics;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Browsing.Subscriptions;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Browsing.Subscriptions;

public sealed class AuthenticatedSubscriptionsServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";

    [Fact]
    public async Task LoadFirstFeedPageAsync_MapsSuccessfulResults()
    {
        var runner = new FakeRunner(startInfo =>
        {
            Assert.Contains("https://www.youtube.com/feed/subscriptions", startInfo.ArgumentList);
            return Task.FromResult(new ProcessResult(0, GenerateFeedJson(20), string.Empty));
        });
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstFeedPageAsync(20, CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.Success, result.Status);
        Assert.Equal(20, result.FeedPage.Videos.Count);
        Assert.Equal("21", result.FeedPage.ContinuationToken);
    }

    [Fact]
    public async Task LoadFirstFeedPageAsync_AuthenticationRequiredWhenNoSession()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, string.Empty, string.Empty)));
        var session = new InMemorySessionService();
        var cookieProvider = new FakeCookieFileProvider();
        var preferences = new TestPreferences();
        var service = new AuthenticatedSubscriptionsService(session, cookieProvider, preferences, runner);

        var result = await service.LoadFirstFeedPageAsync(20, CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.AuthenticationRequired, result.Status);
        Assert.Empty(result.FeedPage.Videos);
    }

    [Fact]
    public async Task LoadFirstFeedPageAsync_BackendFailureReturnsAppropriateStatus()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, string.Empty, "error")));
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstFeedPageAsync(20, CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.TemporaryBackendFailure, result.Status);
    }

    [Fact]
    public async Task LoadFirstFeedPageAsync_EmptyResultsHandledCleanly()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "{\"entries\": []}", string.Empty)));
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstFeedPageAsync(20, CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.Empty, result.Status);
        Assert.Empty(result.FeedPage.Videos);
    }

    [Fact]
    public async Task LoadNextFeedPageAsync_UsesContinuationAndOnlyReturnsNewPage()
    {
        var calls = 0;
        var runner = new FakeRunner(startInfo =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Contains("--playlist-start", startInfo.ArgumentList);
                Assert.Contains("1", startInfo.ArgumentList);
                return Task.FromResult(new ProcessResult(0, GenerateFeedJson(20, "page1"), string.Empty));
            }

            Assert.Contains("--playlist-start", startInfo.ArgumentList);
            Assert.Contains("21", startInfo.ArgumentList);
            return Task.FromResult(new ProcessResult(0, GenerateFeedJson(20, "page2"), string.Empty));
        });
        var (service, _) = CreateService(runner);

        var first = await service.LoadFirstFeedPageAsync(20, CancellationToken.None);
        var second = await service.LoadNextFeedPageAsync(20, CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.Success, first.Status);
        Assert.Equal(AuthenticatedSubscriptionsStatus.Success, second.Status);
        Assert.Equal("page2_1".PadLeft(11, '0'), second.FeedPage.Videos[0].Id);
    }

    [Fact]
    public async Task LoadSubscribedChannelsAsync_MapsSuccessfulResults()
    {
        var runner = new FakeRunner(startInfo =>
        {
            Assert.Contains("https://www.youtube.com/feed/channels", startInfo.ArgumentList);
            return Task.FromResult(new ProcessResult(0, GenerateChannelsJson(5), string.Empty));
        });
        var (service, _) = CreateService(runner);

        var result = await service.LoadSubscribedChannelsAsync(CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.Success, result.Status);
        Assert.Equal(5, result.Channels.Count);
        Assert.Equal("Channel 1", result.Channels[0].Title);
        Assert.Equal("https://www.youtube.com/channel/UC_chan_1", result.Channels[0].Url);
    }

    [Fact]
    public async Task LoadSubscribedChannelsAsync_AuthenticationRequiredWhenNoSession()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, string.Empty, string.Empty)));
        var session = new InMemorySessionService();
        var cookieProvider = new FakeCookieFileProvider();
        var preferences = new TestPreferences();
        var service = new AuthenticatedSubscriptionsService(session, cookieProvider, preferences, runner);

        var result = await service.LoadSubscribedChannelsAsync(CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.AuthenticationRequired, result.Status);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public async Task LoadSubscribedChannelsAsync_BackendFailureReturnsAppropriateStatus()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, string.Empty, "error")));
        var (service, _) = CreateService(runner);

        var result = await service.LoadSubscribedChannelsAsync(CancellationToken.None);

        Assert.Equal(AuthenticatedSubscriptionsStatus.TemporaryBackendFailure, result.Status);
    }

    [Fact]
    public async Task SessionChanged_ClearsCachedResults()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, GenerateFeedJson(5), string.Empty)));
        var (service, session) = CreateService(runner);

        await service.LoadFirstFeedPageAsync(5, CancellationToken.None);
        session.ClearSession();

        var result = await service.LoadNextFeedPageAsync(5, CancellationToken.None);
        Assert.Equal(AuthenticatedSubscriptionsStatus.AuthenticationRequired, result.Status);
    }

    private static (AuthenticatedSubscriptionsService Service, InMemorySessionService Session) CreateService(
        IYtDlpRunner runner)
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        var cookieProvider = new FakeCookieFileProvider();
        var preferences = new TestPreferences();
        var service = new AuthenticatedSubscriptionsService(session, cookieProvider, preferences, runner);
        return (service, session);
    }

    private static string GenerateFeedJson(int count, string prefix = "vid")
    {
        var entries = Enumerable.Range(1, count).Select(i =>
        {
            var id = $"{prefix}_{i}".PadLeft(11, '0');
            return
                $$"""{"id": "{{id}}", "webpage_url": "https://www.youtube.com/watch?v={{id}}", "title": "Video {{i}}", "uploader": "Channel", "duration": 180}""";
        });
        return $$"""{"entries": [{{string.Join(",", entries)}}]}""";
    }

    private static string GenerateChannelsJson(int count)
    {
        var entries = Enumerable.Range(1, count).Select(i =>
            $$"""{"id": "UC_chan_{{i}}", "title": "Channel {{i}}", "url": "https://www.youtube.com/channel/UC_chan_{{i}}", "thumbnail": "https://img.example/chan_{{i}}.jpg"}""");
        return $$"""{"entries": [{{string.Join(",", entries)}}]}""";
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

    private sealed class FakeCookieFileProvider : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile()
        {
            return new CookieFileLease("/tmp/fake_cookie_file");
        }
    }

    private sealed class FakeRunner(Func<ProcessStartInfo, Task<ProcessResult>> handler) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return handler(startInfo);
        }
    }
}
