using SilverScreen.Browsing.Home;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Browsing.Home;

public sealed class HomeFeedCoordinatorTests
{
    private const string FakeCookieContent = "test-manual-session";

    private static async Task WaitForStateAsync(HomeFeedCoordinator coordinator, Func<HomeFeedState, bool> predicate,
        TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<HomeFeedState> handler = (_, state) =>
        {
            if (predicate(state)) tcs.TrySetResult();
        };
        coordinator.StateChanged += handler;
        if (predicate(coordinator.State))
        {
            coordinator.StateChanged -= handler;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => tcs.TrySetCanceled());
            await tcs.Task;
        }
        finally
        {
            coordinator.StateChanged -= handler;
        }
    }

    private static VideoSummary CreateTestVideo(string id, bool isShort = false)
    {
        return new VideoSummary(
            id,
            "Test Video " + id,
            "Test Channel " + id,
            TimeSpan.FromMinutes(5),
            "https://example.com/thumb_" + id + ".jpg",
            isShort,
            "https://example.com/watch?v=" + id
        );
    }


    [Fact]
    public async Task LoadMoreAsync_AppendsUniqueVideos_HidesContinuationWhenDone_CallsNextOnlyWithSavedToken()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        var firstPageTcs = fakeFeed.ExpectLoadFirstPage();
        var v1 = CreateTestVideo("1");
        var v2 = CreateTestVideo("2");
        firstPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { v1, v2 }, "token_next_1"),
            "Success"
        ));

        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;
        await WaitForStateAsync(coordinator, s => s.Kind == HomeFeedStateKind.Ready, TimeSpan.FromSeconds(5));

        Assert.True(coordinator.State.HasContinuation);
        Assert.Equal(1, fakeFeed.LoadFirstPageCallCount);
        Assert.Equal(0, fakeFeed.LoadNextPageCallCount);

        // Prepare page 2: video 2 (duplicate ID) and video 3 (unique)
        var nextPageTcs1 = fakeFeed.ExpectLoadNextPage();
        var v3 = CreateTestVideo("3");
        nextPageTcs1.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { v2, v3 }, "token_next_2"),
            "Success"
        ));

        // Act - Load More page 2
        var loadMoreTask1 = coordinator.LoadMoreAsync();
        await fakeFeed.NextPageCalledTask;
        await loadMoreTask1;

        // Assert - Unique IDs appended
        Assert.Equal(3, coordinator.State.Videos.Length);
        Assert.Equal(["1", "2", "3"], coordinator.State.Videos.Select(v => v.Id));
        Assert.True(coordinator.State.HasContinuation);
        Assert.Equal(1, fakeFeed.LoadNextPageCallCount);

        // Reset tracking TCS for nextPage call
        fakeFeed.ResetCalledTasks();

        // Prepare page 3: video 4 with no continuation token (end of continuation)
        var nextPageTcs2 = fakeFeed.ExpectLoadNextPage();
        var v4 = CreateTestVideo("4");
        nextPageTcs2.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { v4 }),
            "Success"
        ));

        // Act - Load More page 3
        var loadMoreTask2 = coordinator.LoadMoreAsync();
        await fakeFeed.NextPageCalledTask;
        await loadMoreTask2;

        // Assert - End of continuation hides continuation
        Assert.Equal(4, coordinator.State.Videos.Length);
        Assert.Equal(["1", "2", "3", "4"], coordinator.State.Videos.Select(v => v.Id));
        Assert.False(coordinator.State.HasContinuation);
        Assert.Equal(2, fakeFeed.LoadNextPageCallCount);

        // Act - Load More page 4 when continuation token is null/empty
        await coordinator.LoadMoreAsync();

        // Assert - Calls next only with saved token (no call made because continuation was null/empty)
        Assert.Equal(2, fakeFeed.LoadNextPageCallCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenAuthRequired_MapsSafeMessageAndClearsCards()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        // Step 1: Successful load to populate videos
        var firstPageTcs = fakeFeed.ExpectLoadFirstPage();
        firstPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { CreateTestVideo("1") }),
            "Success"
        ));

        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;
        await WaitForStateAsync(coordinator, s => s.Kind == HomeFeedStateKind.Ready, TimeSpan.FromSeconds(5));

        Assert.NotEmpty(coordinator.State.Videos);

        // Step 2: Refresh again, returning AuthenticationRequired
        fakeFeed.ResetCalledTasks();
        var refreshPageTcs = fakeFeed.ExpectLoadFirstPage();
        refreshPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.AuthenticationRequired,
            FeedPage.Empty,
            "Internal backend error message that should not be shown to user"
        ));

        // Act
        var refreshTask = coordinator.RefreshAsync();
        await fakeFeed.FirstPageCalledTask;
        await refreshTask;

        // Assert
        Assert.Equal(HomeFeedStateKind.AuthenticationRequired, coordinator.State.Kind);
        Assert.Empty(coordinator.State.Videos);
        Assert.Equal("Your YouTube session is no longer valid.", coordinator.State.Message);
    }

    [Fact]
    public async Task ClearSession_ClearsStateAndVideosAndCancelsPendingRequest()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        // Expect load first page but block it
        fakeFeed.ExpectLoadFirstPage();

        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;

        // Act & Assert
        // The cancellation token should not be canceled yet.
        Assert.Single(fakeFeed.FirstPageTokens);
        var cancellationToken = fakeFeed.FirstPageTokens[0];
        Assert.False(cancellationToken.IsCancellationRequested);

        // Clear the session.
        sessionService.ClearSession();

        // The cancellation token must be canceled immediately.
        Assert.True(cancellationToken.IsCancellationRequested);

        // Coordinator state must be SignedOut with empty videos.
        Assert.Equal(HomeFeedStateKind.SignedOut, coordinator.State.Kind);
        Assert.Empty(coordinator.State.Videos);
    }

    [Fact]
    public async Task StaleCanceledRequest_DoesNotOverwriteState()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        // We will trigger two requests.
        // Request 1 is started first and blocked.
        var request1Tcs = fakeFeed.ExpectLoadFirstPage(true);
        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;

        // Reset called task tracker to wait for the second request
        fakeFeed.ResetCalledTasks();

        // Trigger Request 2 (by manually calling RefreshAsync).
        var request2Tcs = fakeFeed.ExpectLoadFirstPage();
        var refreshTask2 = coordinator.RefreshAsync();
        await fakeFeed.FirstPageCalledTask;

        // Complete Request 2 successfully.
        request2Tcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { CreateTestVideo("request2_video") }),
            "Success"
        ));
        await refreshTask2;

        Assert.Equal(HomeFeedStateKind.Ready, coordinator.State.Kind);
        Assert.Single(coordinator.State.Videos);
        Assert.Equal("request2_video", coordinator.State.Videos[0].Id);

        // Now complete Request 1 (which is stale/canceled).
        request1Tcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { CreateTestVideo("request1_video") }),
            "Success"
        ));

        // Wait a brief moment or yield control.
        await Task.Yield();

        // Verify that the stale Request 1 result did not overwrite coordinator state.
        Assert.Equal(HomeFeedStateKind.Ready, coordinator.State.Kind);
        Assert.Single(coordinator.State.Videos);
        Assert.Equal("request2_video", coordinator.State.Videos[0].Id);
    }

    [Fact]
    public async Task RefreshAsync_OnTemporaryFailure_PreservesExistingVideosAndSetsSafeError()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        // Step 1: Successful load to populate videos
        var firstPageTcs = fakeFeed.ExpectLoadFirstPage();
        firstPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(new List<VideoSummary> { CreateTestVideo("1"), CreateTestVideo("2") }),
            "Success"
        ));

        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;
        await WaitForStateAsync(coordinator, s => s.Kind == HomeFeedStateKind.Ready, TimeSpan.FromSeconds(5));

        Assert.Equal(2, coordinator.State.Videos.Length);

        // Step 2: Refresh again, returning TemporaryBackendFailure
        fakeFeed.ResetCalledTasks();
        var refreshPageTcs = fakeFeed.ExpectLoadFirstPage();
        refreshPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.TemporaryBackendFailure,
            FeedPage.Empty,
            "Internal network timeout message"
        ));

        // Act
        var refreshTask = coordinator.RefreshAsync();
        await fakeFeed.FirstPageCalledTask;
        await refreshTask;

        // Assert
        Assert.Equal(HomeFeedStateKind.SafeError, coordinator.State.Kind);
        Assert.Equal("Could not load YouTube recommendations.", coordinator.State.Message);
        // Existing video cards must be preserved!
        Assert.Equal(2, coordinator.State.Videos.Length);
        Assert.Equal(["1", "2"], coordinator.State.Videos.Select(v => v.Id));
    }
    [Fact]
    public async Task RefreshAndLoadMore_WithCustomCount_PropagatesCountToService()
    {
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);
        var fakeFeed = new FakeAuthenticatedHomeFeedService();

        var firstPageTcs = fakeFeed.ExpectLoadFirstPage();
        firstPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage([CreateTestVideo("1")], "token_1"),
            "Success"));

        using var coordinator = new HomeFeedCoordinator(sessionService, fakeFeed);
        await fakeFeed.FirstPageCalledTask;
        await WaitForStateAsync(coordinator, s => s.Kind == HomeFeedStateKind.Ready, TimeSpan.FromSeconds(5));

        fakeFeed.ResetCalledTasks();
        var nextPageTcs = fakeFeed.ExpectLoadNextPage();
        nextPageTcs.SetResult(new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage([CreateTestVideo("2")], "token_2"),
            "Success"));

        await coordinator.LoadMoreAsync(count: 40);
        Assert.Equal(40, Assert.Single(fakeFeed.NextPageCounts));
    }


    private sealed class FakeAuthenticatedHomeFeedService : IAuthenticatedHomeFeedService
    {
        private readonly Queue<(TaskCompletionSource<AuthenticatedHomeFeedResult> Tcs, bool IgnoreCancellation)>
            _firstPageTcsQueue = new();

        private readonly Queue<(TaskCompletionSource<AuthenticatedHomeFeedResult> Tcs, bool IgnoreCancellation)>
            _nextPageTcsQueue = new();

        private TaskCompletionSource _firstPageCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource _nextPageCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int LoadFirstPageCallCount { get; private set; }
        public int LoadNextPageCallCount { get; private set; }
        public List<CancellationToken> FirstPageTokens { get; } = [];
        public List<int> FirstPageCounts { get; } = [];
        public List<int> NextPageCounts { get; } = [];

        public Task FirstPageCalledTask => _firstPageCalledTcs.Task;
        public Task NextPageCalledTask => _nextPageCalledTcs.Task;

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            LoadFirstPageCallCount++;
            FirstPageTokens.Add(cancellationToken);
            FirstPageCounts.Add(count);
            _firstPageCalledTcs.TrySetResult();

            if (_firstPageTcsQueue.Count == 0)
                return Task.FromResult(new AuthenticatedHomeFeedResult(
                    AuthenticatedHomeFeedStatus.Success,
                    FeedPage.Empty,
                    "Success"
                ));

            var (tcs, ignoreCancellation) = _firstPageTcsQueue.Dequeue();
            if (ignoreCancellation) return tcs.Task;
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            return tcs.Task;
        }

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            LoadNextPageCallCount++;
            NextPageCounts.Add(count);
            _nextPageCalledTcs.TrySetResult();

            if (_nextPageTcsQueue.Count == 0)
                return Task.FromResult(new AuthenticatedHomeFeedResult(
                    AuthenticatedHomeFeedStatus.Success,
                    FeedPage.Empty,
                    "Success"
                ));
            var (tcs, ignoreCancellation) = _nextPageTcsQueue.Dequeue();
            if (ignoreCancellation) return tcs.Task;
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            return tcs.Task;
        }

        public FeedPage GetHomeFeed()
        {
            return FeedPage.Empty;
        }

        public void ResetCalledTasks()
        {
            _firstPageCalledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _nextPageCalledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource<AuthenticatedHomeFeedResult> ExpectLoadFirstPage(bool ignoreCancellation = false)
        {
            var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            _firstPageTcsQueue.Enqueue((tcs, ignoreCancellation));
            return tcs;
        }

        public TaskCompletionSource<AuthenticatedHomeFeedResult> ExpectLoadNextPage(bool ignoreCancellation = false)
        {
            var tcs = new TaskCompletionSource<AuthenticatedHomeFeedResult>(TaskCreationOptions
                .RunContinuationsAsynchronously);
            _nextPageTcsQueue.Enqueue((tcs, ignoreCancellation));
            return tcs;
        }
    }
}