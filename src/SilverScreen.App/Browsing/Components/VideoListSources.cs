using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Subscriptions;

namespace SilverScreen.Browsing.Components;

public sealed class HomeVideoListSource : IVideoListSource
{
    private readonly HomeFeedCoordinator _coordinator;
    private bool _disposed;

    public HomeVideoListSource(HomeFeedCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _coordinator.StateChanged += OnStateChanged;
    }

    public VideoListPresentationState State => MapState(_coordinator.State);

    public event EventHandler<VideoListPresentationState>? StateChanged;

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _coordinator.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _coordinator.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(HomeFeedState state)
    {
        var (description, icon) = state.Kind switch
        {
            HomeFeedStateKind.SignedOut => (SessionGate.HomeSignedOutMessage,
                "avatar-default-symbolic"),
            HomeFeedStateKind.Empty or HomeFeedStateKind.Ready => (SessionGate.HomeEmptyMessage,
                "applications-internet-symbolic"),
            HomeFeedStateKind.AuthenticationRequired => (SessionGate.SessionNoLongerValidMessage,
                "dialog-password-symbolic"),
            _ => (SessionGate.HomeLoadErrorMessage, "network-error-symbolic")
        };

        var status = new VideoListStatus(
            "Home",
            description,
            icon);

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status);
    }

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }
}

public sealed class SearchVideoListSource : IVideoListSource
{
    private readonly SearchViewModel _viewModel;
    private bool _disposed;

    public SearchVideoListSource(SearchViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModel.StateChanged += OnStateChanged;
    }

    public VideoListPresentationState State => MapState(_viewModel.State);

    public event EventHandler<VideoListPresentationState>? StateChanged;

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(SearchViewState state, string? paginationError = null)
    {
        var safePaginationError = string.IsNullOrWhiteSpace(paginationError)
            ? null
            : "YouTube couldn’t load more search results. Try again in a moment.";
        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            MapStatus(state),
            "Searching YouTube…",
            "Loading more results…",
            safePaginationError);
    }

    public static VideoListStatus MapStatus(FeedEngineState state)
    {
        if (!state.IsSuccess || state.LastError != null)
        {
            var description = "YouTube couldn’t complete the search. Try again in a moment.";
            return new VideoListStatus(
                "Could not complete search",
                description,
                "network-error-symbolic",
                true);
        }

        const string emptyDescription = "Try different keywords or check spelling.";

        return new VideoListStatus(
            "No results found",
            emptyDescription,
            "system-search-symbolic");
    }

    private static VideoListStatus MapStatus(SearchViewState state)
    {
        return state.IsSuccess
            ? new VideoListStatus(
                "No results found",
                "Try different keywords or check spelling.",
                "system-search-symbolic")
            : new VideoListStatus(
                "Could not complete search",
                "YouTube couldn’t complete the search. Try again in a moment.",
                "network-error-symbolic",
                true);
    }

    private void OnStateChanged(object? sender, SearchViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }
}

public sealed class HistoryVideoListSource : IVideoListSource
{
    private readonly HistoryViewModel _viewModel;
    private bool _disposed;

    public HistoryVideoListSource(HistoryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModel.StateChanged += OnStateChanged;
    }

    public VideoListPresentationState State => MapState(_viewModel.State);

    public event EventHandler<VideoListPresentationState>? StateChanged;

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(HistoryViewState state, string? paginationError = null)
    {
        var safePaginationError = string.IsNullOrWhiteSpace(paginationError)
            ? null
            : "YouTube couldn’t load more watch history. Try again in a moment.";
        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            MapStatus(state.Status, state.IsSuccess, state.Summary),
            "Loading watch history…",
            "Loading more history…",
            safePaginationError);
    }

    public static VideoListStatus MapStatus(AuthenticatedHistoryStatus historyStatus, FeedEngineState state)
    {
        return MapStatus(
            state.LastError != null ? AuthenticatedHistoryStatus.TemporaryBackendFailure : historyStatus,
            state is { IsSuccess: true, LastError: null },
            state.StatusMessage);
    }

    private static VideoListStatus MapStatus(AuthenticatedHistoryStatus status, bool isSuccess, string? summary)
    {
        return status switch
        {
            AuthenticatedHistoryStatus.AuthenticationRequired or AuthenticatedHistoryStatus.AuthenticationRejected =>
                new VideoListStatus(
                    SessionGate.HistorySignedOutTitle,
                    status == AuthenticatedHistoryStatus.AuthenticationRejected
                        ? SessionGate.SessionNoLongerValidMessage
                        : SessionGate.HistorySignedOutMessage,
                    "avatar-default-symbolic",
                    false,
                    status == AuthenticatedHistoryStatus.AuthenticationRejected
                        ? SessionGate.SignInAgainActionLabel
                        : null),
            AuthenticatedHistoryStatus.TemporaryBackendFailure =>
                new VideoListStatus(
                    SessionGate.HistoryErrorTitle,
                    SessionGate.HistoryErrorMessage,
                    "network-error-symbolic",
                    true),
            _ when !isSuccess =>
                new VideoListStatus(
                    SessionGate.HistoryErrorTitle,
                    SessionGate.HistoryErrorMessage,
                    "network-error-symbolic",
                    true),
            _ => new VideoListStatus(
                SessionGate.HistoryEmptyTitle,
                SessionGate.HistoryEmptyMessage,
                "document-open-recent-symbolic")
        };
    }

    private void OnStateChanged(object? sender, HistoryViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }
}

public sealed class ChannelVideoListSource : IVideoListSource
{
    private readonly ChannelViewModel _viewModel;
    private bool _disposed;

    public ChannelVideoListSource(ChannelViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModel.StateChanged += OnStateChanged;
    }

    public VideoListPresentationState State => MapState(_viewModel.State);

    public event EventHandler<VideoListPresentationState>? StateChanged;

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(ChannelViewState state, string? paginationError = null)
    {
        var safePaginationError = string.IsNullOrWhiteSpace(paginationError)
            ? null
            : "YouTube couldn’t load more channel videos. Try again in a moment.";
        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            MapStatus(state),
            "Loading channel…",
            "Loading more videos…",
            safePaginationError);
    }

    public static VideoListStatus MapStatus(FeedEngineState state)
    {
        if (!state.IsSuccess || state.LastError != null)
        {
            const string description = "YouTube couldn’t load this channel. Try again in a moment.";

            return new VideoListStatus(
                "Could not load channel",
                description,
                "network-error-symbolic",
                true);
        }

        const string emptyDescription = "This channel does not have any public videos available right now.";
        return new VideoListStatus(
            "No videos found",
            emptyDescription,
            "applications-internet-symbolic");
    }

    private static VideoListStatus MapStatus(ChannelViewState state)
    {
        if (!state.IsSuccess)
        {
            const string description = "YouTube couldn’t load this channel. Try again in a moment.";
            return new VideoListStatus(
                "Could not load channel",
                description,
                "network-error-symbolic",
                true);
        }

        const string emptyDescription = "This channel does not have any public videos available right now.";
        return new VideoListStatus(
            "No videos found",
            emptyDescription,
            "applications-internet-symbolic");
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }
}

public sealed class SubscriptionsVideoListSource : IVideoListSource
{
    private readonly Action? _openWebLogin;
    private readonly SubscriptionsViewModel _viewModel;
    private bool _disposed;

    public SubscriptionsVideoListSource(SubscriptionsViewModel viewModel, Action? openWebLogin = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _openWebLogin = openWebLogin;
        _viewModel.StateChanged += OnStateChanged;
    }

    public VideoListPresentationState State => MapState(_viewModel.State, _openWebLogin);

    public event EventHandler<VideoListPresentationState>? StateChanged;

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        return _viewModel.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(SubscriptionsViewState state, Action? openWebLogin = null,
        string? paginationError = null)
    {
        var safePaginationError = string.IsNullOrWhiteSpace(paginationError ?? state.PaginationError)
            ? null
            : "YouTube couldn’t load more subscription videos. Try again in a moment.";
        VideoListStatus status;
        switch (state.Status)
        {
            case AuthenticatedSubscriptionsStatus.AuthenticationRequired:
            case AuthenticatedSubscriptionsStatus.AuthenticationRejected:
                status = new VideoListStatus(
                    SessionGate.SubscriptionsSignedOutTitle,
                    state.Status == AuthenticatedSubscriptionsStatus.AuthenticationRejected
                        ? SessionGate.SessionNoLongerValidMessage
                        : SessionGate.SubscriptionsSignedOutMessage,
                    "avatar-default-symbolic",
                    false,
                    state.Status == AuthenticatedSubscriptionsStatus.AuthenticationRejected
                        ? SessionGate.SignInAgainActionLabel
                        : SessionGate.SignInActionLabel,
                    openWebLogin);
                break;

            case AuthenticatedSubscriptionsStatus.TemporaryBackendFailure:
                status = new VideoListStatus(
                    SessionGate.SubscriptionsErrorTitle,
                    SessionGate.SubscriptionsErrorMessage,
                    "network-error-symbolic",
                    true);
                break;

            case AuthenticatedSubscriptionsStatus.Empty:
            case AuthenticatedSubscriptionsStatus.Success:
            default:
                if (!state.IsSuccess)
                {
                    status = new VideoListStatus(
                        SessionGate.SubscriptionsErrorTitle,
                        SessionGate.SubscriptionsErrorMessage,
                        "network-error-symbolic",
                        true);
                }
                else if (state.Videos.Count == 0)
                {
                    if (state.SelectedChannel is not null)
                        status = new VideoListStatus(
                            "No videos found",
                            $"No videos found for {state.SelectedChannel.Title}.",
                            "video-x-generic-symbolic");
                    else
                        status = new VideoListStatus(
                            SessionGate.SubscriptionsEmptyTitle,
                            SessionGate.SubscriptionsEmptyMessage,
                            "emblem-favorite-symbolic");
                }
                else
                {
                    status = new VideoListStatus(
                        "Subscriptions",
                        string.Empty,
                        "emblem-favorite-symbolic");
                }

                break;
        }

        const string loadingMessage = "Loading subscriptions…";

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status,
            loadingMessage,
            "Loading more videos…",
            safePaginationError);
    }
    private void OnStateChanged(object? sender, SubscriptionsViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state, _openWebLogin));
    }
}