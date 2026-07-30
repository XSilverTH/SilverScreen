using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Queue;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task SearchSupersedesPriorRequest_WithoutChangingHomePage()
    {
        var service = new ControlledSearchService();
        var shell = new ShellViewModel();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService(), shell);

        var first = viewModel.SubmitAsync("first query");
        Assert.Single(service.Requests);
        var firstRequest = service.Requests[0];
        var second = viewModel.SubmitAsync("second query");
        Assert.Equal(2, service.Requests.Count);
        Assert.True(firstRequest.Token.IsCancellationRequested);

        var video = new VideoSummary("abc123def45", "Second", "Channel", TimeSpan.FromMinutes(2), "", false);
        service.Requests[1].Completion.SetResult(new SearchResultPage([video]));
        await second;

        Assert.Equal("home", shell.SelectedPage);
        Assert.Equal("Search complete.", shell.Status);
        Assert.False(viewModel.State.IsLoading);
        Assert.Equal(new[] { video }, viewModel.State.Videos);
        firstRequest.Completion.TrySetCanceled();
        await first;
    }

    [Fact]
    public async Task ResetCancelsPendingSearchAndClearsResults()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService(), new CapturingStatusReporter());

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
    public void QueuePresentationTracksChanges_AndUnsubscribesOnDispose()
    {
        var queue = new QueueService();
        var viewModel = new QueueViewModel(queue, new FakePlaybackService(), new CapturingStatusReporter());
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
        var statusReporter = new CapturingStatusReporter();
        using var viewModel = new QueueViewModel(queue, playback, statusReporter);

        var launch = viewModel.PlayAllAsync();
        var duplicateLaunch = viewModel.PlayAllAsync();

        Assert.True(viewModel.State.IsLaunching);
        Assert.False(viewModel.State.CanPlay);
        Assert.Single(playback.Requests);
        Assert.Equal(new[] { first.Video, second.Video }, playback.Requests[0].Videos.ToArray());
        await duplicateLaunch;

        playback.Completion.SetResult("MPV opened.");
        await launch;

        Assert.Equal("MPV opened.", statusReporter.Message);
        Assert.False(viewModel.State.IsLaunching);
        Assert.Equal([first.Id, second.Id], queue.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task QueuePlayAllReportsUnexpectedErrors()
    {
        var queue = new QueueService();
        queue.Add(new VideoSummary("abc123_X-yZ", "First", "Channel", TimeSpan.FromMinutes(2), "", false));
        var playback = new ControlledPlaybackService();
        var statusReporter = new CapturingStatusReporter();
        using var viewModel = new QueueViewModel(queue, playback, statusReporter);

        var launch = viewModel.PlayAllAsync();
        playback.Completion.SetException(new InvalidOperationException());
        await launch;

        Assert.Equal("Playback could not be started.", statusReporter.Message);
        Assert.False(viewModel.State.IsLaunching);
        Assert.Single(queue.Items);
    }


    [Fact]
    public async Task SearchProjectsOnlyUniqueNonShortVideos()
    {
        var service = new ControlledSearchService();
        using var viewModel = new SearchViewModel(service, new FakePlaybackService(), new CapturingStatusReporter());
        var first = new VideoSummary("abc123def45", "First", "Channel", TimeSpan.FromMinutes(2), "", false);
        var duplicate = first with { Title = "Duplicate" };
        var shortVideo = new VideoSummary("def456ghi78", "Short", "Channel", TimeSpan.FromMinutes(1), "", true);

        var search = viewModel.SubmitAsync("query");
        service.Requests[0].Completion.SetResult(new SearchResultPage([first, duplicate, shortVideo]));
        await search;

        Assert.Equal([first], viewModel.State.Videos);
    }

    [Fact]
    public void QueueItems_ExposeReadOnlyViewWithoutSuppressingChanges()
    {
        var queue = new QueueService();
        var changes = 0;
        queue.Changed += (_, _) => changes++;
        var video = new VideoSummary("abc123_X-yZ", "Video", "Channel", TimeSpan.FromMinutes(1), "", false);

        queue.Add(video);

        Assert.IsNotType<List<QueueItem>>(queue.Items);
        var items = Assert.IsAssignableFrom<IList<QueueItem>>(queue.Items);
        Assert.Throws<NotSupportedException>(() => items.Add(new QueueItem(Guid.NewGuid(), video, DateTimeOffset.Now)));
        queue.Clear();
        Assert.Equal(2, changes);
    }
    [Fact]
    public void QueueAdd_AppendsVideoToBeginningOrEndCorrectly()
    {
        var queue = new QueueService();
        var first = new VideoSummary("video1", "First Video", "Channel 1", TimeSpan.FromMinutes(1), "", false);
        var second = new VideoSummary("video2", "Second Video", "Channel 2", TimeSpan.FromMinutes(2), "", false);

        queue.Add(first);
        queue.Add(second);

        Assert.Equal(2, queue.Items.Count);
        Assert.Equal("video1", queue.Items[0].Video.Id);
        Assert.Equal("video2", queue.Items[1].Video.Id);
    }

    private sealed class ControlledSearchService : ISearchService
    {
        public List<(string Query, CancellationToken Token, TaskCompletionSource<SearchResultPage> Completion)> Requests
        {
            get;
        } = [];

        public Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            var completion =
                new TaskCompletionSource<SearchResultPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request.Query, cancellationToken, completion));
            return completion.Task;
        }

        public bool IsLikelyYouTubeUrl(string text)
        {
            return false;
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

    private sealed class CapturingStatusReporter : IStatusReporter
    {
        public string? Message { get; private set; }

        public void ReportStatus(string message)
        {
            Message = message;
        }
    }
}