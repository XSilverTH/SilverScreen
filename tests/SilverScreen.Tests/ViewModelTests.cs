using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Queue;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class ViewModelTests
{
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
        service.Requests[1].Completion.SetResult(new SearchResultPage([video]));
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
        var viewModel = new QueueViewModel(queue, new FakePlaybackService(), new ShellViewModel());
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
        var second = queue.Add(new VideoSummary("dQw4w9WgXcQ", "Second", "Channel", TimeSpan.FromMinutes(3), "", false));
        var playback = new ControlledPlaybackService();
        var shell = new ShellViewModel();
        using var viewModel = new QueueViewModel(queue, playback, shell);

        var launch = viewModel.PlayAllAsync();
        var duplicateLaunch = viewModel.PlayAllAsync();

        Assert.True(viewModel.State.IsLaunching);
        Assert.False(viewModel.State.CanPlay);
        Assert.Single(playback.Requests);
        Assert.Equal(new[] { first.Video, second.Video }, playback.Requests[0].Videos.ToArray());
        await duplicateLaunch;

        playback.Completion.SetResult("MPV opened.");
        await launch;

        Assert.Equal("MPV opened.", shell.Status);
        Assert.False(viewModel.State.IsLaunching);
        Assert.Equal([first.Id, second.Id], queue.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task QueuePlayAllReportsUnexpectedErrors()
    {
        var queue = new QueueService();
        queue.Add(new VideoSummary("abc123_X-yZ", "First", "Channel", TimeSpan.FromMinutes(2), "", false));
        var playback = new ControlledPlaybackService();
        var shell = new ShellViewModel();
        using var viewModel = new QueueViewModel(queue, playback, shell);

        var launch = viewModel.PlayAllAsync();
        playback.Completion.SetException(new InvalidOperationException());
        await launch;

        Assert.Equal("Playback could not be started.", shell.Status);
        Assert.False(viewModel.State.IsLaunching);
        Assert.Single(queue.Items);
    }

    [Fact]
    public void HomeViewModelReflectsCoordinatorState_AndStopsAfterDispose()
    {
        var session = new InMemorySessionService();
        using var coordinator = new HomeFeedCoordinator(session, new FakeFeedService());
        var viewModel = new HomeViewModel(coordinator);
        Assert.Equal(HomeFeedStateKind.SignedOut, viewModel.State.Kind);

        session.SetManualSession("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tvalue",
            SessionCookieFormat.NetscapeCookiesText);
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

        Assert.False(viewModel.SaveManualSession("  "));
        Assert.Equal("Manual YouTube session was not saved because no cookie content was entered.", shell.Status);
        Assert.False(viewModel.HasManualSession);

        Assert.True(viewModel.SaveManualSession(
            "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tvalue"));
        Assert.True(viewModel.HasManualSession);
        Assert.Equal("Manual YouTube session saved securely.", shell.Status);

        viewModel.ClearSession();
        Assert.False(viewModel.HasManualSession);
        Assert.Equal("YouTube session cleared.", shell.Status);
    }

    [Fact]
    public void AccountViewModelSavesCapturedWebSession()
    {
        var session = new InMemorySessionService();
        var shell = new ShellViewModel();
        var validation = new SessionValidationCoordinator(new HomeSessionValidator(new FakeFeedService()), session);
        using var viewModel = new AccountViewModel(session, validation, shell);

        Assert.False(viewModel.SaveWebSession("  "));
        Assert.Equal("YouTube web session was not saved because no cookie content was captured.", shell.Status);
        Assert.False(viewModel.HasManualSession);

        Assert.True(viewModel.SaveWebSession(
            "  # Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\tvalue  "));
        Assert.True(viewModel.HasManualSession);
        Assert.Equal("YouTube web session saved securely.", shell.Status);
        Assert.Equal(
            "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\tvalue",
            session.GetManualSessionCookies()?.Content);
    }

    [Fact]
    public void AccountViewModelReportsKeyringPersistenceFailures()
    {
        var session = new FailingSessionService { FailSave = true };
        var shell = new ShellViewModel();
        var validation = new SessionValidationCoordinator(new HomeSessionValidator(new FakeFeedService()), session);
        using var viewModel = new AccountViewModel(session, validation, shell);

        Assert.False(viewModel.SaveManualSession("cookie"));
        Assert.Equal("Manual YouTube session could not be saved because the system keyring is unavailable.",
            shell.Status);
        Assert.False(viewModel.HasManualSession);

        session.FailSave = false;
        Assert.True(viewModel.SaveManualSession("cookie"));
        Assert.Equal("Manual YouTube session saved securely.", shell.Status);
        Assert.True(viewModel.HasManualSession);

        session.FailClear = true;
        viewModel.ClearSession();
        Assert.Equal("YouTube session could not be cleared because the system keyring is unavailable.", shell.Status);
        Assert.True(viewModel.HasManualSession);

        session.FailClear = false;
        session.FailSave = true;
        var existingContent = session.GetManualSessionCookies()?.Content;
        Assert.False(viewModel.SaveWebSession("replacement"));
        Assert.Equal("YouTube session could not be saved because the system keyring is unavailable.", shell.Status);
        Assert.Equal(existingContent, session.GetManualSessionCookies()?.Content);
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

    private sealed class FakeFeedService : IAuthenticatedHomeFeedService
    {
        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty,
                "Empty"));
        }

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty,
                "Empty"));
        }

        public FeedPage GetHomeFeed()
        {
            return FeedPage.Empty;
        }
    }

    private sealed class FailingSessionService : ISessionService
    {
        private ManualSessionCookies? _cookies;

        public bool FailSave { get; set; }
        public bool FailClear { get; set; }

        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession()
        {
            return _cookies is null
                ? AccountSession.SignedOut
                : new AccountSession(true, "YouTube session", HasManualSession: true, CookieFormat: _cookies.Format);
        }

        public ManualSessionCookies? GetManualSessionCookies()
        {
            return _cookies;
        }

        public void SetManualSession(string cookieContent, SessionCookieFormat format)
        {
            if (FailSave) throw new SessionPersistenceException();

            _cookies = new ManualSessionCookies(format, cookieContent);
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSession()
        {
            if (FailClear) throw new SessionPersistenceException();

            _cookies = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}