using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Session;
using Xunit;

namespace SilverScreen.Tests;

public sealed class SessionValidationCoordinatorTests
{
    private const string FakeCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tfake-session-value\n";

    private sealed class FakeAuthenticatedHomeFeedService : IAuthenticatedHomeFeedService
    {
        public Func<CancellationToken, Task<AuthenticatedHomeFeedResult>>? LoadFirstPageAsyncHandler { get; set; }
        public int LoadFirstPageCallCount { get; private set; }

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            LoadFirstPageCallCount++;
            if (LoadFirstPageAsyncHandler != null)
            {
                return LoadFirstPageAsyncHandler(cancellationToken);
            }

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

    [Fact]
    public async Task SignedOut_IsAvailableFalse_NoBackendCall()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        var fakeFeed = new FakeAuthenticatedHomeFeedService();
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act
        bool isAvailable = coordinator.IsAvailable;
        string result = await coordinator.ValidateAsync();

        // Assert
        Assert.False(isAvailable);
        Assert.Equal(SessionValidationFormatter.NoActiveSessionMessage, result);
        Assert.Equal(0, fakeFeed.LoadFirstPageCallCount);
    }

    [Fact]
    public void ActiveManualSession_IsAvailableTrue()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act
        bool isAvailable = coordinator.IsAvailable;

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task SuccessfulValidation_SafeSummaryFields()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService
        {
            LoadFirstPageAsyncHandler = token => Task.FromResult(new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Success,
                new FeedPage(new List<VideoSummary>
                {
                    new("v1", "Test Title 1", "Channel 1", TimeSpan.FromMinutes(5), "https://example.com/thumb1.jpg",
                        false),
                    new("v2", "Test Title 2", "Channel 2", TimeSpan.FromMinutes(3), "https://example.com/thumb2.jpg",
                        false)
                }, "token-xyz"),
                "Fake status message"
            ))
        };
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act
        string formattedResult = await coordinator.ValidateAsync();

        // Assert
        Assert.Equal(1, fakeFeed.LoadFirstPageCallCount);
        Assert.Contains("Validation succeeded.", formattedResult);
        Assert.Contains("Usable videos: 2", formattedResult);
        Assert.Contains("Continuation available: yes", formattedResult);
        Assert.Contains("Authentication required: no", formattedResult);
        Assert.Contains("Status: Recommendations loaded.", formattedResult);
        Assert.True(coordinator.IsAvailable);
        Assert.False(coordinator.IsValidating);
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
            LoadFirstPageAsyncHandler = token => tcs.Task
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
        string duplicateResult = await coordinator.ValidateAsync();

        // Complete the first validation
        tcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary>
            {
                new("v1", "Title", "Channel", TimeSpan.FromMinutes(1), "thumb", false)
            }),
            "Done"
        ));

        string firstResult = await task1;

        // Assert
        Assert.Equal(SessionValidationFormatter.AlreadyRunningMessage, duplicateResult);
        Assert.Contains("Validation succeeded.", firstResult);
        Assert.Contains("Usable videos: 1", firstResult);
        Assert.Equal(1, fakeFeed.LoadFirstPageCallCount);
        Assert.False(coordinator.IsValidating);
        Assert.True(coordinator.IsAvailable);
    }

    [Fact]
    public async Task CancellationPropagation()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>();
        var fakeFeed = new FakeAuthenticatedHomeFeedService
        {
            LoadFirstPageAsyncHandler = async token =>
            {
                using (token.Register(() => tcs.TrySetCanceled(token)))
                {
                    return await tcs.Task;
                }
            }
        };
        var validator = new HomeSessionValidator(fakeFeed);
        var coordinator = new SessionValidationCoordinator(validator, sessionService);

        // Act
        var task = coordinator.ValidateAsync();

        // Trigger cancellation on the coordinator
        coordinator.Cancel();

        string result = await task;

        // Assert
        Assert.Equal(SessionValidationFormatter.CancellationMessage, result);
        Assert.False(coordinator.IsValidating);
        Assert.True(coordinator.IsAvailable);
    }

    [Fact]
    public void SafeFormatter_ExcludesStatusMessage_HighLevelStatusMapping()
    {
        // Arrange
        var secretCookieLeak = "COOKIE: SID=fake_secret_cookie_content";
        var resultTemplate = new HomeSessionValidationResult(
            IsSuccess: true,
            VideoCount: 5,
            HasContinuation: true,
            RequiresAuthentication: false,
            HighLevelStatus: AuthenticatedHomeFeedStatus.Success,
            StatusMessage: secretCookieLeak
        );

        // Act & Assert for secret containment
        string formatted = SessionValidationFormatter.FormatResult(resultTemplate);
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
}