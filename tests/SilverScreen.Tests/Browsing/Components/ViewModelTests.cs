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


namespace SilverScreen.Tests.Browsing.Components;

public sealed class ViewModelTests
{
    [Fact]
    public async Task SearchSupersedesPriorRequest_WithoutChangingHomePage()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());

        var first = viewModel.SubmitAsync("first query");
        Assert.Single(service.Requests);
        var firstRequest = service.Requests[0];
        var second = viewModel.SubmitAsync("second query");
        Assert.Equal(2, service.Requests.Count);
        Assert.True(firstRequest.Token.IsCancellationRequested);

        var video = new VideoSummary("abc123def45", "Second", "Channel", TimeSpan.FromMinutes(2), "", false);
        service.Requests[1].Completion.SetResult(new SearchResultPage([video]));
        await second;

        Assert.False(viewModel.State.IsLoading);
        Assert.Equal([video], viewModel.State.Videos);
        firstRequest.Completion.TrySetCanceled();
        await first;
    }

    [Fact]
    public async Task SearchTracksCurrentQuery_AndClearsOnReset()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());

        Assert.Null(viewModel.CurrentQuery);

        var search = viewModel.SubmitAsync("dotnet core tutorials");
        Assert.Equal("dotnet core tutorials", viewModel.CurrentQuery);
        service.Requests[0].Completion.SetResult(new SearchResultPage([]));
        await search;

        Assert.Equal("dotnet core tutorials", viewModel.CurrentQuery);

        viewModel.Reset();
        Assert.Null(viewModel.CurrentQuery);
    }

    [Fact]
    public async Task ResetCancelsPendingSearchAndClearsResults()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());

        var search = viewModel.SubmitAsync("query");
        var request = Assert.Single(service.Requests);

        viewModel.Reset();

        Assert.True(request.Token.IsCancellationRequested);
        Assert.False(viewModel.State.IsLoading);
        Assert.Empty(viewModel.State.Videos);
        Assert.Equal("Search results will appear here.", viewModel.Summary);
        request.Completion.TrySetCanceled();
        await search;
    }

    [Fact]
    public async Task FetchSuggestionsAsync_ReturnsSuggestionsFromService()
    {
        var searchService = new ControlledSearchService();
        var suggestionService = new FakeSearchSuggestionService(["suggestion 1", "suggestion 2"]);
        using var viewModel = new SearchViewModel(searchService, new FakePlaybackService(), suggestionService);

        var suggestions = await viewModel.FetchSuggestionsAsync("query");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("suggestion 1", suggestions[0]);
        Assert.Equal("suggestion 2", suggestions[1]);
    }

    [Fact]
    public async Task FetchSuggestionsAsync_ReturnsEmptyWhenNoServiceOrEmptyQuery()
    {
        var searchService = new ControlledSearchService();
        using var viewModelWithoutService =
            new SearchViewModel(searchService, new FakePlaybackService());
        var withoutService = await viewModelWithoutService.FetchSuggestionsAsync("query");
        Assert.Empty(withoutService);

        var suggestionService = new FakeSearchSuggestionService(["suggestion 1"]);
        using var viewModelWithService = new SearchViewModel(searchService, new FakePlaybackService(), suggestionService);
        var emptyQuery = await viewModelWithService.FetchSuggestionsAsync("   ");
        Assert.Empty(emptyQuery);
    }


    [Fact]
    public void QueuePresentationTracksChanges_AndUnsubscribesOnDispose()
    {
        var queue = new QueueService();
        var viewModel = new QueueViewModel(queue, new FakePlaybackService());
        var changes = 0;
        viewModel.StateChanged += (_, _) => changes++;
        var first = new VideoSummary("abc123def45", "First", "Channel", TimeSpan.FromMinutes(2), "", false);

        queue.Add(first);
        Assert.True(viewModel.State.IsVisible);
        Assert.Equal(TimeSpan.FromMinutes(2), viewModel.State.TotalDuration);
        Assert.Equal(1, changes);

        viewModel.Dispose();
        queue.Clear();
        Assert.Equal(1, changes);
        Assert.Single(viewModel.State.Items);
    }

    [Fact]
    public async Task QueuePlayAllLaunchesOneImmutableOrderedRequestAndKeepsQueue()
    {
        var queue = new QueueService();
        var first = queue.Add(new VideoSummary("abc123_X-yZ", "First", "Channel", TimeSpan.FromMinutes(2), "", false));
        var second =
            queue.Add(new VideoSummary("dQw4w9WgXcQ", "Second", "Channel", TimeSpan.FromMinutes(3), "", false));
        var playback = new ControlledPlaybackService();
        using var viewModel = new QueueViewModel(queue, playback);

        var launch = viewModel.PlayAllAsync();
        var duplicateLaunch = viewModel.PlayAllAsync();

        Assert.True(viewModel.State.IsLaunching);
        Assert.False(viewModel.State.CanPlay);
        Assert.Single(playback.Requests);
        Assert.Equal(new[] { first.Video, second.Video }, playback.Requests[0].Videos.ToArray());
        await duplicateLaunch;

        playback.Completion.SetResult("MPV opened.");
        await launch;

        Assert.False(viewModel.State.IsLaunching);
        Assert.Equal([first.Id, second.Id], queue.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task QueuePlayAllReportsUnexpectedErrors()
    {
        var queue = new QueueService();
        queue.Add(new VideoSummary("abc123_X-yZ", "First", "Channel", TimeSpan.FromMinutes(2), "", false));
        var playback = new ControlledPlaybackService();
        using var viewModel = new QueueViewModel(queue, playback);

        var launch = viewModel.PlayAllAsync();
        playback.Completion.SetException(new InvalidOperationException());
        await launch;

        Assert.False(viewModel.State.IsLaunching);
        Assert.Single(queue.Items);
    }

    [Fact]
    public void QueueViewModelTracksCurrentPlayingIndex()
    {
        var queue = new QueueService();
        queue.Add(new VideoSummary("vid1", "First", "Channel", TimeSpan.FromMinutes(3), "", false));
        queue.Add(new VideoSummary("vid2", "Second", "Channel", TimeSpan.FromMinutes(4), "", false));

        using var viewModel = new QueueViewModel(queue, new FakePlaybackService());
        Assert.Equal(-1, viewModel.State.CurrentPlayingIndex);

        var stateChanges = 0;
        viewModel.StateChanged += (_, state) =>
        {
            stateChanges++;
            Assert.Equal(1, state.CurrentPlayingIndex);
        };

        viewModel.SetCurrentPlayingIndex(1);
        Assert.Equal(1, viewModel.State.CurrentPlayingIndex);
        Assert.Equal(1, stateChanges);

        // Setting same index should be no-op
        viewModel.SetCurrentPlayingIndex(1);
        Assert.Equal(1, stateChanges);
    }

    [Fact]
    public void QueueServiceReplaceAtomicallyUpdatesQueue()
    {
        var queue = new QueueService();
        queue.Add(new VideoSummary("old1", "Old 1", "Channel", TimeSpan.FromMinutes(1), "", false));
        queue.Add(new VideoSummary("old2", "Old 2", "Channel", TimeSpan.FromMinutes(2), "", false));

        var changes = 0;
        queue.Changed += (_, _) => changes++;

        var newVideos = new[]
        {
            new VideoSummary("new1", "New 1", "Channel", TimeSpan.FromMinutes(5), "", false),
            new VideoSummary("new2", "New 2", "Channel", TimeSpan.FromMinutes(10), "", false),
            new VideoSummary("new3", "New 3", "Channel", TimeSpan.FromMinutes(15), "", false)
        };

        queue.Replace(newVideos);

        Assert.Equal(1, changes);
        Assert.Equal(3, queue.Items.Count);
        Assert.Equal(["new1", "new2", "new3"], queue.Items.Select(item => item.Video.Id));
        Assert.Equal(TimeSpan.FromMinutes(30), queue.TotalDuration);
    }


    [Fact]
    public async Task SearchProjectsOnlyUniqueNonShortVideos()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());
        var first = new VideoSummary("abc123def45", "First", "Channel", TimeSpan.FromMinutes(2), "", false);
        var duplicate = first with { Title = "Duplicate" };
        var shortVideo = new VideoSummary("def456ghi78", "Short", "Channel", TimeSpan.FromMinutes(1), "", true);

        var search = viewModel.SubmitAsync("query");
        service.Requests[0].Completion.SetResult(new SearchResultPage([first, duplicate, shortVideo]));
        await search;

        Assert.Equal([first], viewModel.State.Videos);
    }

    [Fact]
    public async Task SearchLoadMoreAsync_AppendsUniqueNextPageVideos()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());
        var first = new VideoSummary("abc123def45", "First", "Channel", TimeSpan.FromMinutes(2), "", false);
        var next = new VideoSummary("def456ghi78", "Next", "Channel", TimeSpan.FromMinutes(2), "", false);

        var search = viewModel.SubmitAsync("query");
        service.Requests[0].Completion.SetResult(new SearchResultPage([first], ContinuationToken: "21"));
        await search;

        var loadMore = viewModel.LoadMoreAsync();
        Assert.Equal(2, service.Requests.Count);
        Assert.Equal(21, service.Requests[1].StartIndex);
        service.Requests[1].Completion.SetResult(new SearchResultPage([first, next]));
        await loadMore;

        Assert.Equal([first, next], viewModel.State.Videos);
        Assert.False(viewModel.State.HasMore);
    }

    [Fact]
    public async Task SearchFailure_SetsIsSuccessFalse()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());

        var search = viewModel.SubmitAsync("query");
        service.Requests[0].Completion.SetResult(new SearchResultPage([], "Failed to load results.", false, null));
        await search;

        Assert.False(viewModel.State.IsSuccess);
        Assert.Equal("Failed to load results.", viewModel.State.Summary);
        Assert.Empty(viewModel.State.Videos);
    }

    [Fact]
    public async Task SearchException_SetsIsSuccessFalse()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService());

        var search = viewModel.SubmitAsync("query");
        service.Requests[0].Completion.SetException(new InvalidOperationException("Network down"));
        await search;

        Assert.False(viewModel.State.IsSuccess);
        Assert.Equal("Search could not be completed.", viewModel.State.Summary);
        Assert.Empty(viewModel.State.Videos);
    }


    private sealed class ControlledSearchService : ISearchService
    {
        public List<(string Query, int StartIndex, CancellationToken Token, TaskCompletionSource<SearchResultPage>
                Completion)>
            Requests { get; } = [];

        public Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            var completion =
                new TaskCompletionSource<SearchResultPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request.Query, request.StartIndex, cancellationToken, completion));
            return completion.Task;
        }


    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request)
        {
            return Task.FromResult("Playback started.");
        }
    }

    private sealed class ControlledPlaybackService : IPlaybackService
    {
        public List<PlaybackRequest> Requests { get; } = [];

        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> PlayAsync(PlaybackRequest request)
        {
            Requests.Add(request);
            return Completion.Task;
        }
    }

    private sealed class FakeSearchSuggestionService(IReadOnlyList<string> suggestions) : ISearchSuggestionService
    {
        public Task<IReadOnlyList<string>> GetSuggestionsAsync(string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(suggestions);
        }
    }
}
