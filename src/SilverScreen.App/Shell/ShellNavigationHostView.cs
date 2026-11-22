using Adw;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Shell;

public partial class ShellNavigationHostView : ViewBase<ViewStack>
{
    private static readonly ILogger Logger = Log.ForContext<ShellNavigationHostView>();

    private readonly ChannelView _channel;
    private readonly ChannelViewModel _channelViewModel;
    private readonly Dictionary<NavigationEntry, ChannelBrowsingState> _channelBrowsingStates = [];
    private readonly VideoListView _history;
    private readonly HistoryViewModel _historyViewModel;
    private readonly VideoListView _home;
    private readonly INavigationService _navigationService;
    private readonly IShellRefreshCoordinator _refreshCoordinator;
    private readonly VideoListView _searchView;
    private readonly SearchViewModel _searchViewModel;
    private readonly Dictionary<NavigationEntry, SearchBrowsingState> _searchBrowsingStates = [];
    private readonly SubscriptionsView _subscriptions;
    private readonly SubscriptionsViewModel _subscriptionsViewModel;

    public ViewStack ViewStack => Widget;

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
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _refreshCoordinator = refreshCoordinator ?? throw new ArgumentNullException(nameof(refreshCoordinator));
        _searchViewModel = searchViewModel ?? throw new ArgumentNullException(nameof(searchViewModel));

        var cardServices = new VideoCardServices(browsing.Thumbnails, _navigationService, playback, account.Queue, notificationService);

        _channelViewModel = Lifetime.Own(new ChannelViewModel(browsing.Channels));
        _channel = Lifetime.Own(new ChannelView(_channelViewModel, cardServices));
        Lifetime.Track(() => _channel.RefreshLoadingChanged += OnPageLoadingChanged, () => _channel.RefreshLoadingChanged -= OnPageLoadingChanged);
        channel_host.Append(_channel.Widget);

        _home = Lifetime.Own(new VideoListView(browsing.HomeFeed.GetVideoListSource(() => account.Auth.StartAuthAsync().FireAndForget(Logger)), cardServices));
        Lifetime.Track(() => _home.RefreshLoadingChanged += OnPageLoadingChanged, () => _home.RefreshLoadingChanged -= OnPageLoadingChanged);
        home_host.Append(_home.Widget);

        _historyViewModel = Lifetime.Own(new HistoryViewModel(browsing.History, account.Session, () => account.Auth.StartAuthAsync().FireAndForget(Logger)));
        _history = Lifetime.Own(new VideoListView(_historyViewModel, cardServices));
        Lifetime.Track(() => _history.RefreshLoadingChanged += OnPageLoadingChanged, () => _history.RefreshLoadingChanged -= OnPageLoadingChanged);
        history_host.Append(_history.Widget);

        _subscriptionsViewModel = Lifetime.Own(new SubscriptionsViewModel(browsing.Subscriptions, browsing.Channels, account.Session, true, () => account.Auth.StartAuthAsync().FireAndForget(Logger), account.Auth));
        _subscriptions = Lifetime.Own(new SubscriptionsView(_subscriptionsViewModel, cardServices));
        Lifetime.Track(() => _subscriptions.RefreshLoadingChanged += OnPageLoadingChanged, () => _subscriptions.RefreshLoadingChanged -= OnPageLoadingChanged);
        subscriptions_host.Append(_subscriptions.Widget);

        _searchView = Lifetime.Own(new VideoListView(_searchViewModel, cardServices));
        Lifetime.Track(() => _searchView.RefreshLoadingChanged += OnPageLoadingChanged, () => _searchView.RefreshLoadingChanged -= OnPageLoadingChanged);
        search_host.Append(_searchView.Widget);

        _searchViewModel.OpenChannelRequested = channelTarget =>
        {
            var url = channelTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? channelTarget : $"https://www.youtube.com/{channelTarget.TrimStart('/')}";
            _navigationService.NavigateToChannel(url);
            return Task.CompletedTask;
        };

        if (_navigationService is NavigationService concreteNav)
        {
            concreteNav.AttachViewStack(Widget);
            concreteNav.RegisterPage(NavigationPage.Home, "home", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.Subscriptions, "subscriptions", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.History, "history", onEnter: ClearBrowsingHistory);
            concreteNav.RegisterPage(NavigationPage.Search, "search", onEnter: RestoreSearchBrowsingState, onLeave: CaptureSearchBrowsingState);
            concreteNav.RegisterPage(NavigationPage.Channel, "channel", onEnter: RestoreChannelBrowsingState, onLeave: CaptureChannelBrowsingState);
        }

        Lifetime.Track(() => _navigationService.PageChanged += OnNavigationPageChanged, () => _navigationService.PageChanged -= OnNavigationPageChanged);
        _refreshCoordinator.RegisterHandler(RefreshActivePageAsync, () => IsActivePageLoading);

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            Functions.IdleAdd(0, () =>
            {
                if (!IsDisposed) { _navigationService.NavigateToSearch(initialQuery); _searchViewModel.SubmitAsync(initialQuery, _searchView.GetBatchSize()).FireAndForget(Logger); }
                return false;
            });
        }
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
        {
            if (e.CurrentPage == NavigationPage.History) _historyViewModel.LoadAsync(_history.GetBatchSize()).FireAndForget(Logger);
            else if (e.CurrentPage == NavigationPage.Subscriptions) _subscriptionsViewModel.LoadAsync(_subscriptions.GetBatchSize()).FireAndForget(Logger);
        }
        _refreshCoordinator.NotifyLoadingChanged();
    }

    private void OnPageLoadingChanged(object? sender, bool isLoading)
    {
        if (!IsDisposed) _refreshCoordinator.NotifyLoadingChanged();
    }

    public Task RefreshActivePageAsync() => _navigationService.CurrentPage switch
    {
        NavigationPage.Channel => _channel.RefreshAsync(),
        NavigationPage.History => _history.RefreshAsync(),
        NavigationPage.Subscriptions => _subscriptions.RefreshAsync(),
        NavigationPage.Search => _searchView.RefreshAsync(),
        _ => _home.RefreshAsync()
    };

    public bool IsActivePageLoading => _navigationService.CurrentPage switch
    {
        NavigationPage.Channel => _channel.IsLoading,
        NavigationPage.History => _history.IsLoading,
        NavigationPage.Subscriptions => _subscriptions.IsLoading,
        NavigationPage.Search => _searchView.IsLoading,
        _ => _home.IsLoading
    };

    private void ClearBrowsingHistory() { _searchBrowsingStates.Clear(); _channelBrowsingStates.Clear(); }

    private void CaptureSearchBrowsingState() =>
        _searchBrowsingStates[_navigationService.CurrentEntry] = new(_searchViewModel.CaptureNavigationState(), _searchView.Vadjustment?.Value ?? 0);

    private void RestoreSearchBrowsingState()
    {
        if (_searchBrowsingStates.TryGetValue(_navigationService.CurrentEntry, out var state))
        {
            _searchViewModel.RestoreNavigationState(state.ViewModel);
            RestoreScrollPosition(_searchView.Vadjustment, state.ScrollValue);
        }
        else _searchViewModel.Reset();
    }

    private void CaptureChannelBrowsingState() =>
        _channelBrowsingStates[_navigationService.CurrentEntry] = new(_channelViewModel.CaptureNavigationState(), _channel.Vadjustment?.Value ?? 0);

    private void RestoreChannelBrowsingState()
    {
        var entry = _navigationService.CurrentEntry;
        if (_channelBrowsingStates.TryGetValue(entry, out var state))
        {
            _channelViewModel.RestoreNavigationState(state.ViewModel);
            RestoreScrollPosition(_channel.Vadjustment, state.ScrollValue);
            return;
        }
        _channelViewModel.Clear();
        if (entry.Parameter is ChannelNavigationArgs channelArgs)
            _channelViewModel.OpenChannelAsync(channelArgs.Url, channelArgs.Name ?? string.Empty, _channel.GetBatchSize()).FireAndForget(Logger);
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
