using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Tests;

public sealed class SessionValidationCoordinatorTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n";

    [Fact]
    public async Task SignedOut_IsAvailableFalse_NoBackendCall()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        var fakeFeed = new FakeAuthenticatedHomeFeedService();
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act
        var isAvailable = coordinator.IsAvailable;
        var result = await coordinator.ValidateAsync();

        // Assert
        Assert.False(isAvailable);
        Assert.Equal(SessionValidationFormatter.NoActiveSessionMessage, result);
        Assert.Equal(0, fakeFeed.LoadFirstPageCallCount);
    }


    [Fact]
    public async Task DuplicateValidation_Prevention()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>();
        var fakeFeed = new FakeAuthenticatedHomeFeedService
        {
            LoadFirstPageAsyncHandler = _ => tcs.Task
        };
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act & Assert
        // Start first validation
        var task1 = coordinator.ValidateAsync();

        // Verify coordinator status during in-flight validation
        Assert.True(coordinator.IsValidating);
        Assert.False(coordinator.IsAvailable);

        // Start duplicate validation while first is in flight
        var duplicateResult = await coordinator.ValidateAsync();

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
        Assert.False(coordinator.IsValidating);
        Assert.True(coordinator.IsAvailable);
    }


    [Fact]
    public void SafeFormatter_ExcludesStatusMessage_HighLevelStatusMapping()
    {
        // Arrange
        var secretCookieLeak = "COOKIE: SID=fake_secret_cookie_content";
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

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            LoadFirstPageCallCount++;
            if (LoadFirstPageAsyncHandler != null) return LoadFirstPageAsyncHandler(cancellationToken);

            return Task.FromResult(new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Success,
                FeedPage.Empty,
                "Success"
            ));
        }

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public FeedPage GetHomeFeed()
        {
            return FeedPage.Empty;
        }
    }
}