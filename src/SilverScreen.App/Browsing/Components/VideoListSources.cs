using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;
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
        return _coordinator.State is
            { Kind: not HomeFeedStateKind.SignedOut, IsLoading: false, IsLoadingMore: false }
            ? _coordinator.RefreshAsync(count)
            : Task.CompletedTask;
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
            HomeFeedStateKind.SignedOut => ("Sign in to see your YouTube recommendations.",
                "avatar-default-symbolic"),
            HomeFeedStateKind.Empty or HomeFeedStateKind.Ready => ("No recommendations are available right now.",
                "applications-internet-symbolic"),
            HomeFeedStateKind.AuthenticationRequired => ("Your YouTube session is no longer valid.",
                "dialog-password-symbolic"),
            _ => ("Could not load YouTube recommendations.", "network-error-symbolic")
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
        _viewModel.Dispose();
    }

    public static VideoListPresentationState MapState(SearchViewState state)
    {
        VideoListStatus status;
        if (!state.IsSuccess)
        {
            var description = string.IsNullOrWhiteSpace(state.Summary) || state.Summary == "Search failed."
                ? "Failed to load search results. Check your network connection and try again."
                : state.Summary;

            status = new VideoListStatus(
                "Could not complete search",
                description,
                "network-error-symbolic",
                true);
        }
        else
        {
            var description = string.IsNullOrWhiteSpace(state.Summary) || state.Summary == "Search complete."
                ? "Try different keywords or check spelling."
                : state.Summary;

            status = new VideoListStatus(
                "No results found",
                description,
                "system-search-symbolic");
        }

        var loadingMessage = string.IsNullOrWhiteSpace(state.Summary)
            ? "Searching YouTube…"
            : state.Summary;

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status,
            loadingMessage,
            "Loading more results…");
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
        return _viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true }
            ? _viewModel.LoadMoreAsync(count)
            : Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
    }

    public static VideoListPresentationState MapState(HistoryViewState state)
    {
        VideoListStatus status;
        switch (state.Status)
        {
            case AuthenticatedHistoryStatus.AuthenticationRequired:
            case AuthenticatedHistoryStatus.AuthenticationRejected:
                status = new VideoListStatus(
                    "Sign in to see history",
                    !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Watch history requires an active YouTube session.",
                    "avatar-default-symbolic");
                break;

            case AuthenticatedHistoryStatus.TemporaryBackendFailure:
                status = new VideoListStatus(
                    "Could not load history",
                    !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Failed to load your watch history. Check your network connection and try again.",
                    "network-error-symbolic",
                    true);
                break;

            case AuthenticatedHistoryStatus.Empty:
            case AuthenticatedHistoryStatus.Success:
            default:
                if (!state.IsSuccess)
                    status = new VideoListStatus(
                        "Could not load history",
                        !string.IsNullOrWhiteSpace(state.Summary)
                            ? state.Summary
                            : "Failed to load your watch history. Check your network connection and try again.",
                        "network-error-symbolic",
                        true);
                else
                    status = new VideoListStatus(
                        "No watch history",
                        !string.IsNullOrWhiteSpace(state.Summary)
                            ? state.Summary
                            : "Videos you watch on YouTube will appear here.",
                        "document-open-recent-symbolic");
                break;
        }

        var loadingMessage = !string.IsNullOrWhiteSpace(state.Summary)
            ? state.Summary
            : "Loading watch history…";

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status,
            loadingMessage,
            "Loading more history…");
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
        return _viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true }
            ? _viewModel.LoadMoreAsync(count)
            : Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
    }

    public static VideoListPresentationState MapState(ChannelViewState state)
    {
        VideoListStatus status;
        if (!state.IsSuccess)
        {
            var description = string.IsNullOrWhiteSpace(state.Summary) || state.Summary == "Could not load channel."
                ? "Failed to load channel details. Check your network connection and try again."
                : state.Summary;

            status = new VideoListStatus(
                "Could not load channel",
                description,
                "network-error-symbolic",
                true);
        }
        else
        {
            var description = string.IsNullOrWhiteSpace(state.Summary)
                ? "This channel does not have any public videos available right now."
                : state.Summary;

            status = new VideoListStatus(
                "No videos found",
                description,
                "applications-internet-symbolic");
        }

        var loadingMessage = string.IsNullOrWhiteSpace(state.Summary)
            ? "Loading channel…"
            : state.Summary;

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status,
            loadingMessage);
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }
}

public sealed class SubscriptionsVideoListSource : IVideoListSource
{
    private readonly SubscriptionsViewModel _viewModel;
    private readonly Action? _openWebLogin;
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
        return _viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true }
            ? _viewModel.LoadMoreAsync(count)
            : Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
    }

    public static VideoListPresentationState MapState(SubscriptionsViewState state, Action? openWebLogin = null)
    {
        VideoListStatus status;
        switch (state.Status)
        {
            case AuthenticatedSubscriptionsStatus.AuthenticationRequired:
            case AuthenticatedSubscriptionsStatus.AuthenticationRejected:
                status = new VideoListStatus(
                    "Sign in to see subscriptions",
                    !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Subscriptions feed requires an active YouTube session.",
                    "avatar-default-symbolic",
                    false,
                    "Sign In",
                    openWebLogin);
                break;

            case AuthenticatedSubscriptionsStatus.TemporaryBackendFailure:
                status = new VideoListStatus(
                    "Could not load subscriptions",
                    !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Failed to load your subscriptions. Check your network connection and try again.",
                    "network-error-symbolic",
                    true);
                break;

            case AuthenticatedSubscriptionsStatus.Empty:
            case AuthenticatedSubscriptionsStatus.Success:
            default:
                if (!state.IsSuccess)
                {
                    status = new VideoListStatus(
                        "Could not load subscriptions",
                        !string.IsNullOrWhiteSpace(state.Summary)
                            ? state.Summary
                            : "Failed to load your subscriptions. Check your network connection and try again.",
                        "network-error-symbolic",
                        true);
                }
                else if (state.Videos.Count == 0)
                {
                    if (state.SelectedChannel is not null)
                    {
                        status = new VideoListStatus(
                            "No videos found",
                            $"No videos found for {state.SelectedChannel.Title}.",
                            "video-x-generic-symbolic");
                    }
                    else
                    {
                        status = new VideoListStatus(
                            "No subscriptions",
                            !string.IsNullOrWhiteSpace(state.Summary)
                                ? state.Summary
                                : "Channels you subscribe to on YouTube will appear here.",
                            "emblem-favorite-symbolic");
                    }
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

        var loadingMessage = !string.IsNullOrWhiteSpace(state.Summary)
            ? state.Summary
            : "Loading subscriptions…";

        return new VideoListPresentationState(
            state.Videos,
            state.IsLoading,
            state.IsLoadingMore,
            status,
            loadingMessage,
            "Loading more videos…");
    }

    private void OnStateChanged(object? sender, SubscriptionsViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state, _openWebLogin));
    }
}