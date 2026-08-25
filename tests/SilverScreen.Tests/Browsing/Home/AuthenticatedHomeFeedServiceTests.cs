using System.Diagnostics;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Browsing.Home;

public sealed class AuthenticatedHomeFeedServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";

    [Fact]
    public async Task LoadFirstPageAsync_MapsAndCachesSuccessfulResults()
    {
        var runner = new FakeRunner(_ =>
        {
            var output = GenerateYtDlpJson(20);
            return Task.FromResult(new ProcessResult(0, output, ""));
        });
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstPageAsync(20);

        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        Assert.Equal(20, result.FeedPage.Videos.Count);
        Assert.Equal("21", service.GetHomeFeed().ContinuationToken);
        Assert.Equal(20, service.GetHomeFeed().Videos.Count);
    }

    [Fact]
    public async Task LoadFirstPageAsync_AuthenticationRequiredClearsCachedResults()
    {
        var runner = new FakeRunner(_ =>
        {
            var output = GenerateYtDlpJson(2);
            return Task.FromResult(new ProcessResult(0, output, ""));
        });
        var (service, session) = CreateService(runner);
        await service.LoadFirstPageAsync();
        Assert.Equal(2, service.GetHomeFeed().Videos.Count);

        session.ClearSession();

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRequired, result.Status);
        Assert.Empty(service.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_BackendFailurePreservesCachedResults()
    {
        var callCount = 0;
        var runner = new FakeRunner(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var output = GenerateYtDlpJson(2);
                return Task.FromResult(new ProcessResult(0, output, ""));
            }

            return Task.FromResult(new ProcessResult(1, "", "Error"));
        });
        var (service, _) = CreateService(runner);
        await service.LoadFirstPageAsync();
        Assert.Equal(2, service.GetHomeFeed().Videos.Count);

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, result.Status);
        Assert.Equal(2, service.GetHomeFeed().Videos.Count);
    }

    [Fact]
    public async Task LoadNextPageAsync_UsesContinuationAndAppendsToTheCache()
    {
        var runner = new FakeRunner(info =>
        {
            var isNextPage = info.ArgumentList.Contains("21");
            var count = isNextPage ? 5 : 20;
            var prefix = isNextPage ? "page2_" : "page1_";
            var output = GenerateYtDlpJson(count, prefix);
            return Task.FromResult(new ProcessResult(0, output, ""));
        });
        var (service, _) = CreateService(runner);
        await service.LoadFirstPageAsync(20);

        var result = await service.LoadNextPageAsync(20);

        Assert.Equal(5, result.FeedPage.Videos.Count);
        Assert.Equal(25, service.GetHomeFeed().Videos.Count);
    }

    [Fact]
    public async Task LoadFirstPageAsync_RetriesWithoutCookiesWhenZeroVideosReturned()
    {
        var calls = new List<ProcessStartInfo>();
        var runner = new FakeRunner(info =>
        {
            calls.Add(info);
            var output = calls.Count == 1 ? "{\"entries\": []}" : GenerateYtDlpJson(2, "public_");
            return Task.FromResult(new ProcessResult(0, output, ""));
        });
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        Assert.Equal("Public recommendations are displayed.", result.StatusMessage);
        Assert.Equal(2, result.FeedPage.Videos.Count);
        Assert.Equal(2, calls.Count);
        Assert.Contains("--cookies", calls[0].ArgumentList);
        Assert.DoesNotContain("--cookies", calls[1].ArgumentList);
    }

    private static (AuthenticatedHomeFeedService Service, InMemorySessionService Session) CreateService(
        IYtDlpRunner runner)
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        var cookieProvider = new FakeCookieFileProvider();
        var preferences = new TestPreferences();
        var service = new AuthenticatedHomeFeedService(session, cookieProvider, preferences, runner);
        return (service, session);
    }

    private static string GenerateYtDlpJson(int count, string prefix = "vid")
    {
        var entries = Enumerable.Range(1, count).Select(i =>
        {
            var id = $"{prefix}_{i}".PadLeft(11, '0');
            return
                $$"""{"id": "{{id}}", "webpage_url": "https://www.youtube.com/watch?v={{id}}", "title": "Video {{i}}", "uploader": "Channel", "duration": 180}""";
        });
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