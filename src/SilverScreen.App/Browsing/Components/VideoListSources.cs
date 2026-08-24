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
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;

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

    public Task RefreshAsync()
    {
        return _coordinator.State is
            { Kind: not HomeFeedStateKind.SignedOut, IsLoading: false, IsLoadingMore: false }
            ? _coordinator.RefreshAsync()
            : Task.CompletedTask;
    }

    public Task LoadMoreAsync()
    {
        return _coordinator.LoadMoreAsync();
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
            Title: "Home",
            Description: description,
            IconName: icon,
            ShowRetry: false);

        return new VideoListPresentationState(
            Videos: state.Videos,
            IsLoading: state.IsLoading,
            IsLoadingMore: state.IsLoadingMore,
            Status: status,
            LoadingMessage: null,
            PaginationLoadingMessage: "Loading more videos…");
    }

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
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

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }

    public Task LoadMoreAsync()
    {
        return _viewModel.LoadMoreAsync();
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
                Title: "Could not complete search",
                Description: description,
                IconName: "network-error-symbolic",
                ShowRetry: true);
        }
        else
        {
            var description = string.IsNullOrWhiteSpace(state.Summary) || state.Summary == "Search complete."
                ? "Try different keywords or check spelling."
                : state.Summary;

            status = new VideoListStatus(
                Title: "No results found",
                Description: description,
                IconName: "system-search-symbolic",
                ShowRetry: false);
        }

        var loadingMessage = string.IsNullOrWhiteSpace(state.Summary)
            ? "Searching YouTube…"
            : state.Summary;

        return new VideoListPresentationState(
            Videos: state.Videos,
            IsLoading: state.IsLoading,
            IsLoadingMore: state.IsLoadingMore,
            Status: status,
            LoadingMessage: loadingMessage,
            PaginationLoadingMessage: "Loading more results…");
    }

    private void OnStateChanged(object? sender, SearchViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
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

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }

    public Task LoadMoreAsync()
    {
        return _viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true }
            ? _viewModel.LoadMoreAsync()
            : Task.CompletedTask;
    }

    public static VideoListPresentationState MapState(HistoryViewState state)
    {
        VideoListStatus status;
        switch (state.Status)
        {
            case AuthenticatedHistoryStatus.AuthenticationRequired:
            case AuthenticatedHistoryStatus.AuthenticationRejected:
                status = new VideoListStatus(
                    Title: "Sign in to see history",
                    Description: !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Watch history requires an active YouTube session.",
                    IconName: "avatar-default-symbolic",
                    ShowRetry: false);
                break;

            case AuthenticatedHistoryStatus.TemporaryBackendFailure:
                status = new VideoListStatus(
                    Title: "Could not load history",
                    Description: !string.IsNullOrWhiteSpace(state.Summary)
                        ? state.Summary
                        : "Failed to load your watch history. Check your network connection and try again.",
                    IconName: "network-error-symbolic",
                    ShowRetry: true);
                break;

            case AuthenticatedHistoryStatus.Empty:
            case AuthenticatedHistoryStatus.Success:
            default:
                if (!state.IsSuccess)
                {
                    status = new VideoListStatus(
                        Title: "Could not load history",
                        Description: !string.IsNullOrWhiteSpace(state.Summary)
                            ? state.Summary
                            : "Failed to load your watch history. Check your network connection and try again.",
                        IconName: "network-error-symbolic",
                        ShowRetry: true);
                }
                else
                {
                    status = new VideoListStatus(
                        Title: "No watch history",
                        Description: !string.IsNullOrWhiteSpace(state.Summary)
                            ? state.Summary
                            : "Videos you watch on YouTube will appear here.",
                        IconName: "document-open-recent-symbolic",
                        ShowRetry: false);
                }
                break;
        }

        var loadingMessage = !string.IsNullOrWhiteSpace(state.Summary)
            ? state.Summary
            : "Loading watch history…";

        return new VideoListPresentationState(
            Videos: state.Videos,
            IsLoading: state.IsLoading,
            IsLoadingMore: state.IsLoadingMore,
            Status: status,
            LoadingMessage: loadingMessage,
            PaginationLoadingMessage: "Loading more history…");
    }

    private void OnStateChanged(object? sender, HistoryViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
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

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }

    public Task LoadMoreAsync()
    {
        return _viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true }
            ? _viewModel.LoadMoreAsync()
            : Task.CompletedTask;
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
                Title: "Could not load channel",
                Description: description,
                IconName: "network-error-symbolic",
                ShowRetry: true);
        }
        else
        {
            var description = string.IsNullOrWhiteSpace(state.Summary)
                ? "This channel does not have any public videos available right now."
                : state.Summary;

            status = new VideoListStatus(
                Title: "No videos found",
                Description: description,
                IconName: "applications-internet-symbolic",
                ShowRetry: false);
        }

        var loadingMessage = string.IsNullOrWhiteSpace(state.Summary)
            ? "Loading channel…"
            : state.Summary;

        return new VideoListPresentationState(
            Videos: state.Videos,
            IsLoading: state.IsLoading,
            IsLoadingMore: state.IsLoadingMore,
            Status: status,
            LoadingMessage: loadingMessage,
            PaginationLoadingMessage: "Loading more videos…");
    }

    private void OnStateChanged(object? sender, ChannelViewState state)
    {
        if (!_disposed)
            StateChanged?.Invoke(this, MapState(state));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
    }
}
