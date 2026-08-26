using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Session;

public sealed class SessionValidationTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n";

    [Fact]
    public async Task DuplicateValidation_Prevention()
    {
        // Arrange
        var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>();
        var fakeFeed = new FakeAuthenticatedHomeFeedService
        {
            LoadFirstPageAsyncHandler = _ => tcs.Task
        };
        var sessionService = new InMemorySessionService(fakeFeed);
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        // Act & Assert
        // Start first validation
        var task1 = sessionService.ValidateSessionAsync();

        // Verify status during in-flight validation
        Assert.True(sessionService.IsValidating);

        // Start duplicate validation while first is in flight
        var duplicateResult = await sessionService.ValidateSessionAsync();

        // Complete the first validation
        tcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary>
            {
                new("v1", "Title", "Channel", TimeSpan.FromMinutes(1), "thumb", false)
            }),
            "Done"
        ));

        var firstResult = await task1;

        // Assert
        Assert.Equal(SessionValidationFormatter.AlreadyRunningMessage, duplicateResult);
        Assert.Contains("Validation succeeded.", firstResult);
        Assert.Contains("Usable videos: 1", firstResult);
        Assert.Equal(1, fakeFeed.LoadFirstPageCallCount);
        Assert.False(sessionService.IsValidating);
    }

    [Fact]
    public async Task ValidateSessionAsync_WithoutActiveSession_ReturnsNoActiveSessionMessage()
    {
        var sessionService = new InMemorySessionService();
        var result = await sessionService.ValidateSessionAsync();

        Assert.Equal(SessionValidationFormatter.NoActiveSessionMessage, result);
    }

    [Fact]
    public async Task ValidateSessionAsync_Cancellation_ReturnsCancellationMessage()
    {
        var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>();
        var fakeFeed = new FakeAuthenticatedHomeFeedService
        {
            LoadFirstPageAsyncHandler = token =>
            {
                var cancelTcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>();
                token.Register(() => cancelTcs.TrySetCanceled(token));
                return cancelTcs.Task;
            }
        };
        var sessionService = new InMemorySessionService(fakeFeed);
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var task = sessionService.ValidateSessionAsync();
        Assert.True(sessionService.IsValidating);

        sessionService.CancelValidation();

        var result = await task;
        Assert.Equal("Validation canceled.", result);
        Assert.False(sessionService.IsValidating);
    }

    [Fact]
    public void SafeFormatter_ExcludesStatusMessage_HighLevelStatusMapping()
    {
        // Arrange
        const string secretCookieLeak = "COOKIE: SID=fake_secret_cookie_content";
        var resultTemplate = new HomeSessionValidationResult(
            true,
            5,
            true,
            false,
            AuthenticatedHomeFeedStatus.Success,
            secretCookieLeak
        );

        // Act & Assert for secret containment
        var formatted = SessionValidationFormatter.FormatResult(resultTemplate);
        Assert.DoesNotContain(secretCookieLeak, formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SID", formatted, StringComparison.OrdinalIgnoreCase);

        // Act & Assert for high-level status mapping safety
        var statuses = new[]
        {
            (AuthenticatedHomeFeedStatus.Success, "Recommendations loaded."),
            (AuthenticatedHomeFeedStatus.AuthenticationRequired, "A YouTube session is required."),
            (AuthenticatedHomeFeedStatus.AuthenticationRejected, "The YouTube session was rejected or has expired."),
            (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, "Recommendations are temporarily unavailable."),
            (AuthenticatedHomeFeedStatus.Empty, "No usable recommendations were returned."),
            ((AuthenticatedHomeFeedStatus)999, "Validation returned an unknown status.")
        };

        foreach (var (status, expectedText) in statuses)
        {
            var res = resultTemplate with { HighLevelStatus = status };
            var output = SessionValidationFormatter.FormatResult(res);
            Assert.Contains(expectedText, output);
        }
    }

    private sealed class FakeAuthenticatedHomeFeedService : IAuthenticatedHomeFeedService
    {
        public Func<CancellationToken, Task<AuthenticatedHomeFeedResult>>? LoadFirstPageAsyncHandler { get; init; }
        public int LoadFirstPageCallCount { get; private set; }

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            LoadFirstPageCallCount++;
            if (LoadFirstPageAsyncHandler != null) return LoadFirstPageAsyncHandler(cancellationToken);

            return Task.FromResult(new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Success,
                FeedPage.Empty,
                "Success"
            ));
        }

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public FeedPage GetHomeFeed()
        {
            return FeedPage.Empty;
        }
    }
}
