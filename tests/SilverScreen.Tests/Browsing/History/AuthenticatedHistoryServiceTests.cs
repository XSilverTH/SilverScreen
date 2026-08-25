using System.Diagnostics;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Browsing.History;

public sealed class AuthenticatedHistoryServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";

    [Fact]
    public async Task LoadFirstPageAsync_MapsSuccessfulResults()
    {
        var runner = new FakeRunner(_ =>
        {
            var output = GenerateYtDlpJson(20);
            return Task.FromResult(new ProcessResult(0, output, ""));
        });
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstPageAsync(20);

        Assert.Equal(AuthenticatedHistoryStatus.Success, result.Status);
        Assert.Equal(20, result.FeedPage.Videos.Count);
        Assert.Equal("21", result.FeedPage.ContinuationToken);
    }

    [Fact]
    public async Task LoadFirstPageAsync_AuthenticationRequiredWhenNoSession()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, GenerateYtDlpJson(2), "")));
        var (service, session) = CreateService(runner);
        session.ClearSession();

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHistoryStatus.AuthenticationRequired, result.Status);
        Assert.Empty(result.FeedPage.Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_BackendFailureReturnsAppropriateStatus()
    {
        var runner = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, "", "Error")));
        var (service, _) = CreateService(runner);

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHistoryStatus.TemporaryBackendFailure, result.Status);
    }

    [Fact]
    public async Task LoadNextPageAsync_UsesContinuationAndOnlyReturnsNewPage()
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

        Assert.Equal(AuthenticatedHistoryStatus.Success, result.Status);
        Assert.Equal(5, result.FeedPage.Videos.Count);
        Assert.Equal("000page2__1", result.FeedPage.Videos[0].Id);
    }

    private static (AuthenticatedHistoryService Service, InMemorySessionService Session) CreateService(
        IYtDlpRunner runner)
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        var cookieProvider = new FakeCookieFileProvider();
        var preferences = new TestPreferences();
        var service = new AuthenticatedHistoryService(session, cookieProvider, preferences, runner);
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