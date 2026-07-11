using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Feed;
using SilverScreen.Features.Queue;
using SilverScreen.Features.Session;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class ViewModelTests
{
    private sealed class ControlledSearchService : ISearchService
    {
        public List<(string Query, CancellationToken Token, TaskCompletionSource<SearchResultPage> Completion)> Requests { get; } = [];

        public Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<SearchResultPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request.Query, cancellationToken, completion));
            return completion.Task;
        }

        public bool IsLikelyYouTubeUrl(string text) => false;
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request) => Task.FromResult("Playback started.");
    }

    private sealed class FakeFeedService : IAuthenticatedHomeFeedService
    {
        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty, "Empty"));
        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty, "Empty"));
        public FeedPage GetHomeFeed() => FeedPage.Empty;
    }

    [Fact]
    public async Task SearchSupersedesPriorRequest_AndNavigatesToSearch()
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
        service.Requests[1].Completion.SetResult(new SearchResultPage([video], null, true));
        await second;

        Assert.Equal("search", shell.SelectedPage);
        Assert.Equal("Search complete.", shell.Status);
        Assert.False(viewModel.State.IsLoading);
        Assert.Equal(new[] { video }, viewModel.State.Videos);
        firstRequest.Completion.TrySetCanceled();
        await first;
    }

    [Fact]
    public void QueuePresentationTracksChanges_AndUnsubscribesOnDispose()
    {
        var queue = new QueueService();
        var viewModel = new QueueViewModel(queue);
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
    public void HomeViewModelReflectsCoordinatorState_AndStopsAfterDispose()
    {
        var session = new InMemorySessionService();
        using var coordinator = new HomeFeedCoordinator(session, new FakeFeedService());
        var viewModel = new HomeViewModel(coordinator);
        Assert.Equal(HomeFeedStateKind.SignedOut, viewModel.State.Kind);

        session.SetManualSession("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tvalue", SessionCookieFormat.NetscapeCookiesText);
        Assert.Equal(HomeFeedStateKind.Empty, viewModel.State.Kind);

        viewModel.Dispose();
        session.ClearSession();
        Assert.Equal(HomeFeedStateKind.Empty, viewModel.State.Kind);
    }

    [Fact]
    public void AccountViewModelReportsInvalidSessionInput_AndSessionTransitions()
    {
        var session = new InMemorySessionService();
        var shell = new ShellViewModel();
        var validation = new SessionValidationCoordinator(new HomeSessionValidator(new FakeFeedService()), session);
        using var viewModel = new AccountViewModel(session, validation, shell);

        viewModel.SaveManualSession("  ");
        Assert.Equal("Manual YouTube session was not saved because no cookie content was entered.", shell.Status);
        Assert.False(viewModel.HasManualSession);

        viewModel.SaveManualSession("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tvalue");
        Assert.True(viewModel.HasManualSession);
        Assert.Equal("Manual YouTube session active.", shell.Status);

        viewModel.ClearSession();
        Assert.False(viewModel.HasManualSession);
        Assert.Equal("Manual YouTube session cleared.", shell.Status);
    }
}
