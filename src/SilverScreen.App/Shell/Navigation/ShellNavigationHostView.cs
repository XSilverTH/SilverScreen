using Adw;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Shell.Navigation;

public partial class ShellNavigationHostView : ViewBase<ViewStack>
{
    private static readonly ILogger Logger = Log.ForContext<ShellNavigationHostView>();

    private readonly AccountServices _account;
    private readonly BrowsingServices _browsing;
    private readonly VideoCardServices _cardServices;
    private ChannelView? _channel;
    private readonly Dictionary<NavigationEntry, ChannelBrowsingState> _channelBrowsingStates = [];
    private ChannelViewModel? _channelViewModel;
    private VideoListView? _history;
    private HistoryViewModel? _historyViewModel;
    private readonly VideoListView _home;
    private readonly INavigationService _navigationService;
    private readonly IShellRefreshCoordinator _refreshCoordinator;
    private readonly Dictionary<NavigationEntry, SearchBrowsingState> _searchBrowsingStates = [];
    private VideoListView? _searchView;
    private readonly SearchViewModel _searchViewModel;
    private SubscriptionsView? _subscriptions;
    private SubscriptionsViewModel? _subscriptionsViewModel;

    public ShellNavigationHostView(
        BrowsingServices browsing,
        AccountServices account,
        INavigationService navigationService,
        IPlaybackService playback,
        INotificationService notificationService,
        SearchViewModel searchViewModel,
        IShellRefreshCoordinator refreshCoordinator,
        string? initialQuery = null)
    {
        _browsing = browsing ?? throw new ArgumentNullException(nameof(browsing));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _refreshCoordinator = refreshCoordinator ?? throw new ArgumentNullException(nameof(refreshCoordinator));
        _searchViewModel = searchViewModel ?? throw new ArgumentNullException(nameof(searchViewModel));

        _cardServices = new VideoCardServices(browsing.Thumbnails, _navigationService, playback, account.Queue,
            notificationService);

        _home = Lifetime.Own(new VideoListView(
            browsing.HomeFeed.GetVideoListSource(() => account.Auth.StartAuthAsync().FireAndForget(Logger)),
            _cardServices));
        Lifetime.Track(() => _home.RefreshLoadingChanged += OnPageLoadingChanged,
            () => _home.RefreshLoadingChanged -= OnPageLoadingChanged);
        home_host.Append(_home.Widget);

        _searchViewModel.OpenChannelRequested = channelTarget =>
        {
            var url = channelTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? channelTarget
                : $"https://www.youtube.com/{channelTarget.TrimStart('/')}";
            _navigationService.NavigateToChannel(url);
            return Task.CompletedTask;
        };

        if (_navigationService is NavigationService concreteNav)
        {
            concreteNav.AttachViewStack(Widget);
            concreteNav.RegisterPage(NavigationPage.Home, "home", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.Subscriptions, "subscriptions", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.History, "history", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.Search, "search", onEnter: RestoreSearchBrowsingState,
                onLeave: CaptureSearchBrowsingState);
            concreteNav.RegisterPage(NavigationPage.Channel, "channel", onEnter: RestoreChannelBrowsingState,
                onLeave: CaptureChannelBrowsingState);
        }

        Lifetime.Track(() => _navigationService.PageChanged += OnNavigationPageChanged,
            () => _navigationService.PageChanged -= OnNavigationPageChanged);
        _refreshCoordinator.RegisterHandler(RefreshActivePageAsync, () => IsActivePageLoading);

        if (!string.IsNullOrWhiteSpace(initialQuery))
            Functions.IdleAdd(0, () =>
            {
                if (IsDisposed) return false;
                _navigationService.NavigateToSearch(initialQuery);
                _searchViewModel.SubmitAsync(initialQuery, EnsureSearchView().GetBatchSize()).FireAndForget(Logger);

                return false;
            });
    }

    public ViewStack ViewStack => Widget;

    private bool IsActivePageLoading => _navigationService.CurrentPage switch
    {
        NavigationPage.Channel => _channel?.IsLoading ?? false,
        NavigationPage.History => _history?.IsLoading ?? false,
        NavigationPage.Subscriptions => _subscriptions?.IsLoading ?? false,
        NavigationPage.Search => _searchView?.IsLoading ?? false,
        _ => _home.IsLoading
    };

    private ChannelView EnsureChannelView()
    {
        if (_channel is not null)
            return _channel;

        _channelViewModel = Lifetime.Own(new ChannelViewModel(_browsing.Channels));
        var channel = Lifetime.Own(new ChannelView(_channelViewModel, _cardServices));
        Lifetime.Track(() => channel.RefreshLoadingChanged += OnPageLoadingChanged,
            () => channel.RefreshLoadingChanged -= OnPageLoadingChanged);
        channel_host.Append(channel.Widget);
        _channel = channel;
        return channel;
    }

    private VideoListView EnsureHistoryView()
    {
        if (_history is not null)
            return _history;

        _historyViewModel = Lifetime.Own(new HistoryViewModel(_browsing.History, _account.Session,
            () => _account.Auth.StartAuthAsync().FireAndForget(Logger)));
        var history = Lifetime.Own(new VideoListView(_historyViewModel, _cardServices));
        Lifetime.Track(() => history.RefreshLoadingChanged += OnPageLoadingChanged,
            () => history.RefreshLoadingChanged -= OnPageLoadingChanged);
        history_host.Append(history.Widget);
        _history = history;
        return history;
    }

    private SubscriptionsView EnsureSubscriptionsView()
    {
        if (_subscriptions is not null)
            return _subscriptions;

        _subscriptionsViewModel = Lifetime.Own(new SubscriptionsViewModel(_browsing.Subscriptions, _browsing.Channels,
            _account.Session, true, () => _account.Auth.StartAuthAsync().FireAndForget(Logger), _account.Auth));
        var subscriptions = Lifetime.Own(new SubscriptionsView(_subscriptionsViewModel, _cardServices));
        Lifetime.Track(() => subscriptions.RefreshLoadingChanged += OnPageLoadingChanged,
            () => subscriptions.RefreshLoadingChanged -= OnPageLoadingChanged);
        subscriptions_host.Append(subscriptions.Widget);
        _subscriptions = subscriptions;
        return subscriptions;
    }

    private VideoListView EnsureSearchView()
    {
        if (_searchView is not null)
            return _searchView;

        var searchView = Lifetime.Own(new VideoListView(_searchViewModel, _cardServices));
        Lifetime.Track(() => searchView.RefreshLoadingChanged += OnPageLoadingChanged,
            () => searchView.RefreshLoadingChanged -= OnPageLoadingChanged);
        search_host.Append(searchView.Widget);
        _searchView = searchView;
        return searchView;
    }

    private void OnViewStackNotify(object? sender = null, EventArgs? args = null)
    {
        if (!IsDisposed && _navigationService is NavigationService concreteNav)
            concreteNav.SyncFromViewStack(Widget.VisibleChildName);
    }

    private void OnNavigationPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        if (IsDisposed) return;
        if (e.CurrentPage != e.PreviousPage)
            switch (e.CurrentPage)
            {
                case NavigationPage.History:
                    var history = EnsureHistoryView();
                    _historyViewModel!.LoadAsync(history.GetBatchSize()).FireAndForget(Logger);
                    break;
                case NavigationPage.Subscriptions:
                    var subscriptions = EnsureSubscriptionsView();
                    _subscriptionsViewModel!.LoadAsync(subscriptions.GetBatchSize()).FireAndForget(Logger);
                    break;
                case NavigationPage.Search:
                    EnsureSearchView();
                    break;
                case NavigationPage.Channel:
                    EnsureChannelView();
                    break;
            }

        _refreshCoordinator.NotifyLoadingChanged();
    }

    private void OnPageLoadingChanged(object? sender, bool isLoading)
    {
        if (!IsDisposed) _refreshCoordinator.NotifyLoadingChanged();
    }

    private Task RefreshActivePageAsync()
    {
        return _navigationService.CurrentPage switch
        {
            NavigationPage.Channel => EnsureChannelView().RefreshAsync(),
            NavigationPage.History => EnsureHistoryView().RefreshAsync(),
            NavigationPage.Subscriptions => EnsureSubscriptionsView().RefreshAsync(),
            NavigationPage.Search => EnsureSearchView().RefreshAsync(),
            _ => _home.RefreshAsync()
        };
    }

    private void ClearBrowsingHistory()
    {
        _searchBrowsingStates.Clear();
        _channelBrowsingStates.Clear();
    }

    private void CaptureSearchBrowsingState()
    {
        _searchBrowsingStates[_navigationService.CurrentEntry] = new SearchBrowsingState(
            _searchViewModel.CaptureNavigationState(),
            _searchView?.Vadjustment?.Value ?? 0);
    }

    private void RestoreSearchBrowsingState()
    {
        var searchView = EnsureSearchView();
        if (_searchBrowsingStates.TryGetValue(_navigationService.CurrentEntry, out var state))
        {
            _searchViewModel.RestoreNavigationState(state.ViewModel);
            RestoreScrollPosition(searchView.Vadjustment, state.ScrollValue);
        }
        else
        {
            _searchViewModel.Reset();
        }
    }

    private void CaptureChannelBrowsingState()
    {
        if (_channelViewModel is null)
            return;

        _channelBrowsingStates[_navigationService.CurrentEntry] = new ChannelBrowsingState(
            _channelViewModel.CaptureNavigationState(),
            _channel?.Vadjustment?.Value ?? 0);
    }

    private void RestoreChannelBrowsingState()
    {
        var channelView = EnsureChannelView();
        var entry = _navigationService.CurrentEntry;
        if (_channelBrowsingStates.TryGetValue(entry, out var state))
        {
            _channelViewModel!.RestoreNavigationState(state.ViewModel);
            RestoreScrollPosition(channelView.Vadjustment, state.ScrollValue);
            return;
        }

        _channelViewModel!.Clear();
        if (entry.Parameter is ChannelNavigationArgs channelArgs)
            _channelViewModel
                .OpenChannelAsync(channelArgs.Url, channelArgs.Name ?? string.Empty, channelView.GetBatchSize())
                .FireAndForget(Logger);
    }

    private static void RestoreScrollPosition(Adjustment? adjustment, double value)
    {
        Functions.IdleAdd(0, () =>
        {
            if (adjustment is null) return false;
            var maximum = Math.Max(adjustment.Lower, adjustment.Upper - adjustment.PageSize);
            adjustment.SetValue(Math.Clamp(value, adjustment.Lower, maximum));
            return false;
        });
    }

    private sealed record SearchBrowsingState(SearchNavigationState ViewModel, double ScrollValue);

    private sealed record ChannelBrowsingState(ChannelNavigationState ViewModel, double ScrollValue);
}