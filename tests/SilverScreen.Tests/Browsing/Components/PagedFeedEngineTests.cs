using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Tests.Browsing.Components;

public sealed class PagedFeedEngineTests
{
    private static VideoSummary CreateVideo(string id, bool isShort = false)
    {
        return new VideoSummary(
            id,
            $"Video {id}",
            "Channel",
            TimeSpan.FromMinutes(3),
            $"https://example.com/thumb/{id}.jpg",
            isShort,
            $"https://example.com/watch?v={id}");
    }

    [Fact]
    public void InitialState_IsCorrectlyDefaulted()
    {
        using var engine = new PagedFeedEngine(
            defaultTitle: "Custom Feed",
            defaultEmptyTitle: "No videos",
            defaultEmptyDescription: "Empty feed description",
            defaultEmptyIcon: "custom-empty-icon");

        Assert.Empty(engine.Videos);
        Assert.False(engine.IsLoading);
        Assert.False(engine.IsLoadingMore);
        Assert.False(engine.HasMore);
        Assert.Null(engine.ContinuationToken);
        Assert.True(engine.IsSuccess);

        Assert.Equal("No videos", engine.State.Status.Title);
        Assert.Equal("Empty feed description", engine.State.Status.Description);
        Assert.Equal("custom-empty-icon", engine.State.Status.IconName);
    }

    [Fact]
    public async Task RefreshAsync_PopulatesVideos_FiltersShorts_AndDeduplicates()
    {
        var v1 = CreateVideo("1");
        var v1Dup = CreateVideo("1");
        var v2 = CreateVideo("2");
        var shortVideo = CreateVideo("short", isShort: true);

        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) => Task.FromResult(new FeedPageResult(
                [v1, v1Dup, v2, shortVideo],
                ContinuationToken: "token_page_2")));

        var stateChanges = 0;
        engine.StateChanged += (_, _) => stateChanges++;

        await engine.RefreshAsync();

        Assert.Equal(["1", "2"], engine.Videos.Select(v => v.Id));
        Assert.Equal("token_page_2", engine.ContinuationToken);
        Assert.True(engine.HasMore);
        Assert.False(engine.IsLoading);
        Assert.False(engine.IsLoadingMore);
        Assert.True(engine.IsSuccess);
        Assert.True(stateChanges >= 2); // Initial loading state + final loaded state
    }

    [Fact]
    public async Task LoadMoreAsync_PassesContinuationToken_AndAppendsUniqueVideos()
    {
        var passedTokens = new List<string?>();
        var page = 1;

        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                passedTokens.Add(token);
                if (page == 1)
                {
                    page++;
                    return Task.FromResult(new FeedPageResult(
                        [CreateVideo("1"), CreateVideo("2")],
                        ContinuationToken: "token_2"));
                }

                return Task.FromResult(new FeedPageResult(
                    [CreateVideo("2"), CreateVideo("3")],
                    ContinuationToken: null));
            });

        await engine.RefreshAsync();
        Assert.Equal([null], passedTokens);
        Assert.Equal(["1", "2"], engine.Videos.Select(v => v.Id));
        Assert.True(engine.HasMore);

        await engine.LoadMoreAsync();
        Assert.Equal([null, "token_2"], passedTokens);
        Assert.Equal(["1", "2", "3"], engine.Videos.Select(v => v.Id));
        Assert.False(engine.HasMore);
        Assert.Null(engine.ContinuationToken);
    }

    [Fact]
    public async Task LoadMoreAsync_GuardsAgainstConcurrentOrInvalidInvocations()
    {
        var fetchTcs = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;

        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                fetchCount++;
                return fetchTcs.Task;
            });

        // 1. When HasMore is false, LoadMore does not invoke fetcher
        await engine.LoadMoreAsync();
        Assert.Equal(0, fetchCount);

        // 2. Start a refresh
        var refreshTask = engine.RefreshAsync();
        Assert.True(engine.IsLoading);

        // 3. LoadMore while IsLoading is true is a no-op
        await engine.LoadMoreAsync();
        Assert.Equal(1, fetchCount);

        fetchTcs.SetResult(new FeedPageResult([CreateVideo("1")], ContinuationToken: "token_2"));
        await refreshTask;

        // 4. Start a LoadMore
        var nextTcs = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Configure((token, count, ct) =>
        {
            fetchCount++;
            return nextTcs.Task;
        });

        var loadMoreTask = engine.LoadMoreAsync();
        Assert.True(engine.IsLoadingMore);

        // 5. Concurrent LoadMore while IsLoadingMore is true is a no-op
        await engine.LoadMoreAsync();
        Assert.Equal(2, fetchCount);

        nextTcs.SetResult(new FeedPageResult([CreateVideo("2")]));
        await loadMoreTask;
    }

    [Fact]
    public async Task ConcurrentRefresh_CancelsPriorRequest_AndDiscardsStaleResults()
    {
        var tcs1 = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        var call = 0;

        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                tokens.Add(ct);
                call++;
                return call == 1 ? tcs1.Task : tcs2.Task;
            });

        var refresh1 = engine.RefreshAsync();
        Assert.Single(tokens);
        Assert.False(tokens[0].IsCancellationRequested);

        // Trigger second refresh
        var refresh2 = engine.RefreshAsync();
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsCancellationRequested); // First token cancelled
        Assert.False(tokens[1].IsCancellationRequested);

        // Complete second request with video "req2"
        tcs2.SetResult(new FeedPageResult([CreateVideo("req2")]));
        await refresh2;

        Assert.Equal(["req2"], engine.Videos.Select(v => v.Id));

        // Stale first request completes with video "req1"
        tcs1.SetResult(new FeedPageResult([CreateVideo("req1")]));
        await refresh1;

        // Verify stale request did not overwrite state
        Assert.Equal(["req2"], engine.Videos.Select(v => v.Id));
    }

    [Fact]
    public async Task ServiceException_SetsErrorState_WithoutCrashing()
    {
        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) => throw new InvalidOperationException("Network down"),
            defaultTitle: "Test Feed");

        await engine.RefreshAsync();

        Assert.False(engine.IsSuccess);
        Assert.False(engine.IsLoading);
        Assert.False(engine.HasMore);
        Assert.NotNull(engine.EngineState.LastError);
        Assert.True(engine.State.Status.ShowRetry);
        Assert.Equal("network-error-symbolic", engine.State.Status.IconName);
    }

    [Fact]
    public async Task FailedRefresh_WithClearExistingFalse_PreservesExistingVideos()
    {
        var page = 1;
        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                if (page == 1)
                {
                    page++;
                    return Task.FromResult(new FeedPageResult([CreateVideo("1"), CreateVideo("2")]));
                }

                return Task.FromResult(FeedPageResult.Failed("Server error", clearExisting: false));
            },
            clearOnRefresh: false);

        await engine.RefreshAsync();
        Assert.Equal(["1", "2"], engine.Videos.Select(v => v.Id));
        Assert.True(engine.IsSuccess);

        // Second refresh fails but preserves videos
        await engine.RefreshAsync();
        Assert.False(engine.IsSuccess);
        Assert.Equal(["1", "2"], engine.Videos.Select(v => v.Id));
    }

    [Fact]
    public async Task FailedRefresh_WithClearExistingTrue_ClearsExistingVideos()
    {
        var page = 1;
        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                if (page == 1)
                {
                    page++;
                    return Task.FromResult(new FeedPageResult([CreateVideo("1"), CreateVideo("2")]));
                }

                return Task.FromResult(FeedPageResult.Failed("Auth required", clearExisting: true));
            },
            clearOnRefresh: false);

        await engine.RefreshAsync();
        Assert.Equal(["1", "2"], engine.Videos.Select(v => v.Id));

        // Second refresh fails with clearExisting: true
        await engine.RefreshAsync();
        Assert.False(engine.IsSuccess);
        Assert.Empty(engine.Videos);
    }

    [Fact]
    public async Task Reset_ClearsVideos_CancelsPendingRequest_AndResetsState()
    {
        var tcs = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;

        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) =>
            {
                capturedToken = ct;
                return tcs.Task;
            });

        var refreshTask = engine.RefreshAsync();
        Assert.True(engine.IsLoading);
        Assert.False(capturedToken.IsCancellationRequested);

        engine.Reset(statusMessage: "Reset message");

        Assert.True(capturedToken.IsCancellationRequested);
        Assert.Empty(engine.Videos);
        Assert.False(engine.IsLoading);
        Assert.False(engine.HasMore);
        Assert.Equal("Reset message", engine.StatusMessage);

        tcs.TrySetCanceled();
        await refreshTask;
    }

    [Fact]
    public void SetVideos_DirectlyUpdatesVideosAndPresentationState()
    {
        using var engine = new PagedFeedEngine();

        var videos = new[] { CreateVideo("v1"), CreateVideo("v2") };
        engine.SetVideos(videos, continuationToken: "token_3", hasMore: true);

        Assert.Equal(["v1", "v2"], engine.Videos.Select(v => v.Id));
        Assert.Equal("token_3", engine.ContinuationToken);
        Assert.True(engine.HasMore);
        Assert.Equal(2, engine.State.Videos.Count);
    }

    [Fact]
    public async Task CustomStatusMapper_OverridesPresentationStatus()
    {
        using var engine = new PagedFeedEngine(
            fetcher: (token, count, ct) => Task.FromResult(new FeedPageResult([CreateVideo("1")])),
            statusMapper: (result, error, state) => new VideoListStatus(
                "Custom Title",
                "Custom Description",
                "custom-icon",
                ShowRetry: true));

        await engine.RefreshAsync();

        Assert.Equal("Custom Title", engine.State.Status.Title);
        Assert.Equal("Custom Description", engine.State.Status.Description);
        Assert.Equal("custom-icon", engine.State.Status.IconName);
        Assert.True(engine.State.Status.ShowRetry);
    }

    [Fact]
    public async Task FactoryMethodCreate_BindsFirstAndNextPageDelegates()
    {
        var firstPageCalled = false;
        var nextPageCalled = false;

        using var engine = PagedFeedEngine.Create(
            fetchFirstPage: (count, ct) =>
            {
                firstPageCalled = true;
                return Task.FromResult("page_1_data");
            },
            fetchNextPage: (token, count, ct) =>
            {
                nextPageCalled = true;
                return Task.FromResult("page_2_data");
            },
            extractResult: raw => new FeedPageResult(
                [CreateVideo(raw)],
                ContinuationToken: raw == "page_1_data" ? "tok2" : null));

        await engine.RefreshAsync();
        Assert.True(firstPageCalled);
        Assert.False(nextPageCalled);
        Assert.Equal(["page_1_data"], engine.Videos.Select(v => v.Id));
        Assert.True(engine.HasMore);

        await engine.LoadMoreAsync();
        Assert.True(nextPageCalled);
        Assert.Equal(["page_1_data", "page_2_data"], engine.Videos.Select(v => v.Id));
        Assert.False(engine.HasMore);
    }

    [Fact]
    public void Dispose_CancelsOngoingRequest_AndPreventsFurtherOperations()
    {
        var tcs = new TaskCompletionSource<FeedPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;

        var engine = new PagedFeedEngine(
            fetcher: (_, _, ct) =>
            {
                token = ct;
                return tcs.Task;
            });

        _ = engine.RefreshAsync();
        Assert.False(token.IsCancellationRequested);

        engine.Dispose();
        Assert.True(token.IsCancellationRequested);
    }
}
