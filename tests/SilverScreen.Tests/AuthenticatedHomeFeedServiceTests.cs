using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.Mock;
using SilverScreen.Infrastructure.YouTube;
using Xunit;

namespace SilverScreen.Tests;

public sealed class AuthenticatedHomeFeedServiceTests
{
    private const string SanitizedCookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsanitized-session-value\n";

    private sealed class FakeYouTubeHomeClient : IYouTubeHomeClient
    {
        public int CallCount { get; private set; }
        public string? LastContinuationToken { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public Func<string?, CancellationToken, Task<HomeFeedResult>>? ResponseFactory { get; set; }

        public Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastContinuationToken = continuationToken;
            LastCancellationToken = cancellationToken;

            if (ResponseFactory != null)
            {
                return ResponseFactory(continuationToken, cancellationToken);
            }

            return Task.FromResult(new HomeFeedResult(Array.Empty<VideoSummary>(), null, true, "Success", false));
        }
    }

    [Fact]
    public async Task LoadFirstPageAsync_NoSession_ReturnsAuthenticationRequiredWithoutClientCall()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Act
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRequired, result.Status);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(FeedPage.Empty, result.FeedPage);
        Assert.Equal("Sign in with a manual YouTube session to load recommendations.", result.StatusMessage);

        // Assert no sensitive value appears in ToString
        Assert.DoesNotContain("sanitized-session-value", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadNextPageAsync_NoSession_ReturnsAuthenticationRequiredWithoutClientCall()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Act
        var result = await feedService.LoadNextPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRequired, result.Status);
        Assert.Equal(0, client.CallCount);
        Assert.Equal(FeedPage.Empty, result.FeedPage);
        Assert.Equal("Sign in with a manual YouTube session to load recommendations.", result.StatusMessage);
    }

    [Fact]
    public async Task LoadFirstPageAsync_WithSession_InvokesClient()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Act
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task LoadFirstPageAsync_SuccessfulResult_MapsFeedPageAndPreservesContinuation()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        var expectedVideos = new List<VideoSummary>
        {
            new("v1", "Test Video 1", "Test Channel 1", TimeSpan.FromMinutes(3), "http://thumb1", false),
            new("v2", "Test Video 2", "Test Channel 2", TimeSpan.FromMinutes(4), "http://thumb2", false)
        };
        client.ResponseFactory = (token, ct) =>
            Task.FromResult(new HomeFeedResult(expectedVideos, "next-token-abc", true, "OK", false));

        // Act
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        Assert.Equal(2, result.FeedPage.Videos.Count);
        Assert.Equal("v1", result.FeedPage.Videos[0].Id);
        Assert.Equal("Test Video 1", result.FeedPage.Videos[0].Title);
        Assert.Equal("next-token-abc", result.FeedPage.ContinuationToken);

        // GetHomeFeed cached behavior
        var cached = feedService.GetHomeFeed();
        Assert.Equal(2, cached.Videos.Count);
        Assert.Equal("next-token-abc", cached.ContinuationToken);

        // Assert no silent MockFeedService fallback
        Assert.IsNotType<MockFeedService>(feedService);
        Assert.Equal("Test Video 1", result.FeedPage.Videos[0].Title);
    }

    [Fact]
    public async Task LoadFirstPageAsync_FiltersShorts()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        var videos = new List<VideoSummary>
        {
            new("v1", "Normal Video", "Channel", TimeSpan.FromMinutes(5), "http://thumb1", false),
            new("v2", "Short Video", "Channel", TimeSpan.FromSeconds(30), "http://thumb2", true)
        };
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(videos, "token", true, "OK", false));

        // Act
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        var returnedVideos = result.FeedPage.Videos;
        Assert.Single(returnedVideos);
        Assert.Equal("v1", returnedVideos[0].Id);
        Assert.False(returnedVideos[0].IsShort);

        var cached = feedService.GetHomeFeed();
        Assert.Single(cached.Videos);
        Assert.Equal("v1", cached.Videos[0].Id);
    }

    [Fact]
    public async Task LoadFirstPageAsync_PublicRecommendationsMessage_PreservesMessage()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        var videos = new List<VideoSummary>
        {
            new("v1", "Ordinary Video", "Channel", TimeSpan.FromMinutes(5), "http://thumb1", false)
        };
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            videos,
            null,
            true,
            "Public recommendations are displayed.",
            false));

        // Act
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        Assert.Single(result.FeedPage.Videos);
        Assert.Equal("v1", result.FeedPage.Videos[0].Id);
        Assert.Equal("Public recommendations are displayed.", result.StatusMessage);
    }

    [Fact]
    public async Task LoadFirstPageAsync_EmptyResult_ClearsCacheAndReturnsEmptyStatus()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Pre-populate cache with a successful call first
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: return empty result from client
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            Array.Empty<VideoSummary>(), null, true, "OK", false));
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Empty, result.Status);
        Assert.Empty(result.FeedPage.Videos);
        Assert.Empty(feedService.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_AllShorts_ClearsCacheAndReturnsEmptyStatus()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Pre-populate cache
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: client returns only Shorts
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v2", "Short", "Channel", TimeSpan.FromSeconds(30), "thumb", true) },
            "token2", true, "OK", false));
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Empty, result.Status);
        Assert.Empty(result.FeedPage.Videos);
        Assert.Empty(feedService.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_AuthenticationRejected_ClearsCacheAndReturnsAuthenticationRejected()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Pre-populate cache
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: client returns Auth Rejected
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            Array.Empty<VideoSummary>(), null, false, "Auth Rejected", true));
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRejected, result.Status);
        Assert.Empty(result.FeedPage.Videos);
        Assert.Empty(feedService.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_BackendFailure_PreservesCacheAndReturnsTemporaryBackendFailure()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Pre-populate cache
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: client returns failure (not requiring auth)
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            Array.Empty<VideoSummary>(), null, false, "Server Error", false));
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, result.Status);
        Assert.Empty(result.FeedPage.Videos);
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_ClientThrowsException_PreservesCacheAndReturnsTemporaryBackendFailure()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Pre-populate cache
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: client throws exception
        client.ResponseFactory = (token, ct) => throw new InvalidOperationException("Something went wrong");
        var result = await feedService.LoadFirstPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, result.Status);
        Assert.Empty(result.FeedPage.Videos);
        Assert.NotEmpty(feedService.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadNextPageAsync_ContinuationRouting_PassesContinuationTokenToClient()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // 1st page
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-xyz", true, "OK", false));
        await feedService.LoadFirstPageAsync();

        // 2nd page config
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v2", "Title 2", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-next", true, "OK", false));

        // Act
        var result = await feedService.LoadNextPageAsync();

        // Assert
        Assert.Equal(2, client.CallCount);
        Assert.Equal("token-xyz", client.LastContinuationToken);
        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);

        // Cumulative cached feed page should contain both v1 and v2, but result FeedPage contains only v2
        Assert.Single(result.FeedPage.Videos);
        Assert.Equal("v2", result.FeedPage.Videos[0].Id);

        var cached = feedService.GetHomeFeed();
        Assert.Equal(2, cached.Videos.Count);
        Assert.Equal("v1", cached.Videos[0].Id);
        Assert.Equal("v2", cached.Videos[1].Id);
        Assert.Equal("token-next", cached.ContinuationToken);
    }

    [Fact]
    public async Task LoadNextPageAsync_NoContinuation_ReturnsEmptyWithoutCallingClient()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        // Prepopulate with a page that has no continuation token (null)
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            null, true, "OK", false));
        await feedService.LoadFirstPageAsync();
        Assert.Equal(1, client.CallCount);

        // Act
        var result = await feedService.LoadNextPageAsync();

        // Assert
        Assert.Equal(AuthenticatedHomeFeedStatus.Empty, result.Status);
        Assert.Equal(1, client.CallCount); // client was not called again
        Assert.Equal("No additional recommendations are available.", result.StatusMessage);

        var cached = feedService.GetHomeFeed();
        Assert.Single(cached.Videos);
        Assert.Equal("v1", cached.Videos[0].Id);
    }

    [Fact]
    public async Task LoadFirstPageAsync_PassesCancellationTokenToClient()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        using var cts = new CancellationTokenSource();

        // Act
        await feedService.LoadFirstPageAsync(cts.Token);

        // Assert
        Assert.Equal(cts.Token, client.LastCancellationToken);
    }

    [Fact]
    public async Task CancellationPropagation_ThrowsOperationCanceledException()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        client.ResponseFactory = (token, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HomeFeedResult(Array.Empty<VideoSummary>(), null, true, "OK", false));
        };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feedService.LoadFirstPageAsync(cts.Token));

        // Set up continuation token to test LoadNextPageAsync cancellation
        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token", true, "OK", false));

        // Load first page to establish continuation token
        await feedService.LoadFirstPageAsync();

        // Act & Assert for NextPage
        client.ResponseFactory = (token, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HomeFeedResult(Array.Empty<VideoSummary>(), null, true, "OK", false));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feedService.LoadNextPageAsync(cts.Token));
    }

    [Fact]
    public async Task CachedPersonalResultsClear_WhenSessionClears()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-xyz", true, "OK", false));
        await feedService.LoadFirstPageAsync();

        Assert.NotEmpty(feedService.GetHomeFeed().Videos);
        Assert.Equal("token-xyz", feedService.GetHomeFeed().ContinuationToken);

        // Act: Clear session
        sessionService.ClearSession();

        // Assert: Cached results in feedService are cleared
        var cached = feedService.GetHomeFeed();
        Assert.Empty(cached.Videos);
        Assert.Null(cached.ContinuationToken);
    }

    [Fact]
    public async Task CachedPersonalResultsClear_WhenSessionChanges()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-xyz", true, "OK", false));
        await feedService.LoadFirstPageAsync();

        Assert.NotEmpty(feedService.GetHomeFeed().Videos);

        // Act: Set new manual session
        sessionService.SetManualSession("another-cookie", SessionCookieFormat.NetscapeCookiesText);

        // Assert: Cached results in feedService are cleared
        var cached = feedService.GetHomeFeed();
        Assert.Empty(cached.Videos);
    }

    [Fact]
    public async Task Validator_ValidateSessionAsync_SuccessCase()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);
        var validator = new HomeSessionValidator(feedService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-xyz", true, "OK", false));

        // Act
        var valResult = await validator.ValidateSessionAsync();

        // Assert
        Assert.True(valResult.IsSuccess);
        Assert.Equal(1, valResult.VideoCount);
        Assert.True(valResult.HasContinuation);
        Assert.False(valResult.RequiresAuthentication);
        Assert.Equal(AuthenticatedHomeFeedStatus.Success, valResult.HighLevelStatus);
        Assert.Equal("Recommendations loaded.", valResult.StatusMessage);

        // Assert no sensitive value in validationResult ToString
        Assert.DoesNotContain("sanitized-session-value", valResult.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validator_ValidateSessionAsync_AuthRequired()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService(); // No session set
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);
        var validator = new HomeSessionValidator(feedService);

        // Act
        var valResult = await validator.ValidateSessionAsync();

        // Assert
        Assert.False(valResult.IsSuccess);
        Assert.Equal(0, valResult.VideoCount);
        Assert.False(valResult.HasContinuation);
        Assert.True(valResult.RequiresAuthentication);
        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRequired, valResult.HighLevelStatus);
        Assert.Equal("Sign in with a manual YouTube session to load recommendations.", valResult.StatusMessage);
    }

    [Fact]
    public async Task Validator_ValidateSessionAsync_AuthenticationRejected()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);
        var validator = new HomeSessionValidator(feedService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            Array.Empty<VideoSummary>(), null, false, "Auth Rejected", true));

        // Act
        var valResult = await validator.ValidateSessionAsync();

        // Assert
        Assert.False(valResult.IsSuccess);
        Assert.Equal(0, valResult.VideoCount);
        Assert.False(valResult.HasContinuation);
        Assert.True(valResult.RequiresAuthentication);
        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRejected, valResult.HighLevelStatus);
        Assert.Equal("The YouTube session was rejected or has expired.", valResult.StatusMessage);
    }

    [Fact]
    public async Task Validator_ValidateSessionAsync_TemporaryBackendFailure()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);
        var validator = new HomeSessionValidator(feedService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            Array.Empty<VideoSummary>(), null, false, "Server Error", false));

        // Act
        var valResult = await validator.ValidateSessionAsync();

        // Assert
        Assert.False(valResult.IsSuccess);
        Assert.Equal(0, valResult.VideoCount);
        Assert.False(valResult.HasContinuation);
        Assert.False(valResult.RequiresAuthentication);
        Assert.Equal(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, valResult.HighLevelStatus);
        Assert.Equal("Recommendations are temporarily unavailable.", valResult.StatusMessage);
    }

    [Fact]
    public async Task ToString_DoesNotContainSensitiveCookieContent()
    {
        // Arrange
        var client = new FakeYouTubeHomeClient();
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(SanitizedCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var feedService = new AuthenticatedHomeFeedService(client, sessionService);
        var validator = new HomeSessionValidator(feedService);

        client.ResponseFactory = (token, ct) => Task.FromResult(new HomeFeedResult(
            new List<VideoSummary> { new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(5), "thumb", false) },
            "token-xyz", true, "OK", false));

        // Act
        var feedResult = await feedService.LoadFirstPageAsync();
        var validationResult = await validator.ValidateSessionAsync();

        // Assert
        Assert.DoesNotContain("sanitized-session-value", feedResult.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sanitized-session-value", validationResult.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Netscape", feedResult.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Netscape", validationResult.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}