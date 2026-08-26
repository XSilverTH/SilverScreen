using Gdk;
using Gio;
using GObject;
using Serilog;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;
using AboutDialog = Adw.AboutDialog;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using Functions = GLib.Functions;
using License = Gtk.License;
using PreferencesDialog = SilverScreen.Preferences.PreferencesDialog;
using Task = System.Threading.Tasks.Task;
using Window = Gtk.Window;

namespace SilverScreen.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();

    private readonly AccountPopoverView _accountPopover;
    private readonly AccountViewModel _accountViewModel;
    private readonly ChannelView _channel;
    private readonly ChannelViewModel _channelViewModel;
    private readonly Action _disposeApplicationServices;
    private readonly EmbeddedPlayerView _embeddedPlayer;
    private readonly VideoListView _history;
    private readonly HistoryViewModel _historyViewModel;
    private readonly VideoListView _home;
    private readonly PlaybackModeRoutingService _playback;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly SearchPopoverView _searchPopover;
    private readonly VideoListView _searchView;
    private readonly SearchViewModel _searchViewModel;
    private readonly ApplicationServices _services;
    private readonly SubscriptionsView _subscriptions;
    private readonly SubscriptionsViewModel _subscriptionsViewModel;
    private bool _closed;
    private string? _lastVisibleChildName;
    private WebLoginWindow? _webLogin;

    public MainWindow(ApplicationServices services, Action disposeApplicationServices)
    {
        Logger.Information("Initializing MainWindow");
        _services = services;
        _disposeApplicationServices = disposeApplicationServices;
        _embeddedPlayer = new EmbeddedPlayerView(OpenEmbeddedPlayer, CloseEmbeddedPlayer,
            video => OpenChannelAsync(video).FireAndForget(Logger), services.Player);
        _playback = new PlaybackModeRoutingService(services.Preferences, services.Playback, _embeddedPlayer);
        player_host.Append(_embeddedPlayer.Widget);
        var actions = CreateVideoActions();
        _channelViewModel = new ChannelViewModel(services.Channels);
        _channel = new ChannelView(_channelViewModel, services.Thumbnails, services.WatchProgress, actions);
        _channel.RefreshLoadingChanged += OnChannelRefreshLoadingChanged;
        _home = new VideoListView(
            services.HomeFeed,
            services.Thumbnails,
            services.WatchProgress,
            actions);
        _home.RefreshLoadingChanged += OnHomeRefreshLoadingChanged;
        _historyViewModel = new HistoryViewModel(services.History);
        _history = new VideoListView(_historyViewModel, services.Thumbnails, services.WatchProgress, actions);
        _history.RefreshLoadingChanged += OnHistoryRefreshLoadingChanged;
        _subscriptionsViewModel = new SubscriptionsViewModel(
            services.Subscriptions,
            services.Channels,
            services.Session,
            true);
        _subscriptions = new SubscriptionsView(
            _subscriptionsViewModel,
            services.Thumbnails,
            services.WatchProgress,
            actions,
            OpenWebLogin,
            (url, name) =>
                OpenChannelAsync(new VideoSummary("", "", name, TimeSpan.Zero, "", false, "", null, null, url))
                    .FireAndForget(Logger));
        _subscriptions.RefreshLoadingChanged += OnSubscriptionsRefreshLoadingChanged;
        subscriptions_host.Append(_subscriptions.Widget);
        history_host.Append(_history.Widget);
        home_host.Append(_home.Widget);
        UpdateHomeRefreshButton(_home.IsLoading);

        _searchViewModel = new SearchViewModel(services.Search, _playback, services.SearchSuggestions);
        _searchPopover = new SearchPopoverView(_searchViewModel, OnSearchSubmitted, search_popover.Popdown);
        search_popover.Child = _searchPopover.Widget;
        search_popover.OnClosed += (_, _) => _searchPopover.OnClosed();
        search_popover.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "visible" && search_popover.GetVisible())
                _searchPopover.OnOpened();
        };

        _searchView = new VideoListView(_searchViewModel, services.Thumbnails, services.WatchProgress, actions);
        _searchView.RefreshLoadingChanged += OnSearchRefreshLoadingChanged;

        _queueViewModel = new QueueViewModel(services.Queue, _playback);
        _queueView = new QueueView(_queueViewModel, services.Thumbnails, services.WatchProgress, CloseQueue);
        queue_sidebar_host.Append(_queueView.Widget);
        _accountViewModel = new AccountViewModel(services.AccountProfile, services.Session);
        _accountPopover = new AccountPopoverView(
            _accountViewModel,
            services.Thumbnails,
            OpenWebLogin,
            UpdateAccountAppearance);

        view_switcher.Stack = view_stack;
        var channelPage = view_stack.AddTitled(_channel.Widget, "channel", "Channel");
        channelPage.Visible = false;
        var searchPage = view_stack.AddTitled(_searchView.Widget, "search", "Search");
        searchPage.Visible = false;
        view_stack.VisibleChildName = "home";

        account_popover.Child = _accountPopover.Widget;
        queue_button.BindProperty("active", queue_split_view, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        RegisterApplicationActions();
        _queueViewModel.StateChanged += OnQueueStateChanged;
        UpdateQueueButton(_queueViewModel.State);
        Widget.OnCloseRequest += OnCloseRequest;
        ReportStartupDependencyWarnings();
    }

    private VideoCardActions CreateVideoActions()
    {
        return new VideoCardActions
        {
            PlayAsync = async video =>
                await _playback.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false),
            OpenInAlternatePlayerAsync = OpenInAlternatePlayerAsync,
            AddToQueue = video => { _services.Queue.Add(video); },
            OpenChannelAsync = OpenChannelAsync
        };
    }

    private async Task OpenInAlternatePlayerAsync(VideoSummary video)
    {
        var request = new PlaybackRequest([video]);
        var playbackBackend = _services.Preferences.GetPreferences().PlaybackBackend;
        if (PlaybackBackends.IsEmbedded(playbackBackend))
            await _services.Playback.PlayAsync(request).ConfigureAwait(false);
        else
            await _embeddedPlayer.PresentAsync(request).ConfigureAwait(false);
    }

    private async Task OpenChannelAsync(VideoSummary video)
    {
        if (string.IsNullOrWhiteSpace(video.ChannelUrl))
            return;

        view_stack.VisibleChildName = "channel";
        UpdateHomeRefreshButton(_channel.IsLoading);
        await _channelViewModel.OpenChannelAsync(video.ChannelUrl, video.ChannelName, _channel.GetBatchSize())
            .ConfigureAwait(false);
    }

    private void CloseChannel()
    {
        _channelViewModel.Clear();
        view_stack.VisibleChildName = "home";
        navigation_back_button.Visible = false;
        UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnSearchSubmitted(string query)
    {
        view_stack.VisibleChildName = "search";
        navigation_back_button.Visible = true;
        UpdateHomeRefreshButton(_searchView.IsLoading);
        _searchViewModel.SubmitAsync(query, _searchView.GetBatchSize()).FireAndForget(Logger);
    }

    private void CloseSearch()
    {
        _searchViewModel.Reset();
        view_stack.VisibleChildName = "home";
        navigation_back_button.Visible = false;
        UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnNavigationBackButtonClicked(object? sender = null, EventArgs? args = null)
    {
        switch (view_stack.VisibleChildName)
        {
            case "search":
                CloseSearch();
                break;
            case "channel":
                CloseChannel();
                break;
            default:
                view_stack.VisibleChildName = "home";
                navigation_back_button.Visible = false;
                break;
        }
    }

    private void OpenEmbeddedPlayer()
    {
        main_stack.VisibleChildName = "player";
        if (_services.Preferences.GetPreferences().OpenInFullscreen)
            Widget.Fullscreen();
    }

    private void CloseEmbeddedPlayer()
    {
        Widget.Unfullscreen();
        main_stack.VisibleChildName = "shell";
    }

    private void ReportStartupDependencyWarnings()
    {
        var warnings = _services.RuntimeDependencyDiagnostics.GetStartupWarnings();
        if (warnings.Count == 0)
            return;

        Logger.Warning("Runtime setup needed: {Warnings}", string.Join(" ", warnings));
    }

    private void OnHomeRefreshButtonClicked(object? sender, EventArgs args)
    {
        switch (view_stack.VisibleChildName)
        {
            case "channel":
                _channel.RefreshAsync().FireAndForget(Logger);
                break;
            case "history":
                _history.RefreshAsync().FireAndForget(Logger);
                break;
            case "subscriptions":
                _subscriptions.RefreshAsync().FireAndForget(Logger);
                break;
            case "search":
                _searchView.RefreshAsync().FireAndForget(Logger);
                break;
            default:
                _home.RefreshAsync().FireAndForget(Logger);
                break;
        }
    }

    private void OnHomeRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && view_stack.VisibleChildName != "channel" && view_stack.VisibleChildName != "history" &&
            view_stack.VisibleChildName != "subscriptions" && view_stack.VisibleChildName != "search")
            UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnChannelRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && view_stack.VisibleChildName == "channel")
            UpdateHomeRefreshButton(_channel.IsLoading);
    }

    private void OnHistoryRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && view_stack.VisibleChildName == "history")
            UpdateHomeRefreshButton(_history.IsLoading);
    }

    private void OnSubscriptionsRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && view_stack.VisibleChildName == "subscriptions")
            UpdateHomeRefreshButton(_subscriptions.IsLoading);
    }

    private void OnSearchRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && view_stack.VisibleChildName == "search")
            UpdateHomeRefreshButton(_searchView.IsLoading);
    }

    private void OnViewStackNotify(object? sender = null, EventArgs? args = null)
    {
        if (_closed) return;
        navigation_back_button.Visible = view_stack.VisibleChildName is "search" or "channel";

        var currentChildName = view_stack.VisibleChildName;
        var childChanged = currentChildName != _lastVisibleChildName;
        _lastVisibleChildName = currentChildName;

        switch (currentChildName)
        {
            case "channel":
                UpdateHomeRefreshButton(_channel.IsLoading);
                break;
            case "history":
                UpdateHomeRefreshButton(_history.IsLoading);
                if (childChanged)
                    _historyViewModel.LoadAsync(_history.GetBatchSize()).FireAndForget(Logger);
                break;
            case "subscriptions":
                UpdateHomeRefreshButton(_subscriptions.IsLoading);
                if (childChanged)
                    _subscriptionsViewModel.LoadAsync(_subscriptions.GetBatchSize()).FireAndForget(Logger);
                break;
            case "search":
                UpdateHomeRefreshButton(_searchView.IsLoading);
                break;
            default:
                UpdateHomeRefreshButton(_home.IsLoading);
                break;
        }
    }

    private void UpdateHomeRefreshButton(bool isLoading)
    {
        home_refresh_button.Sensitive = !isLoading;
        home_refresh_stack.VisibleChildName = isLoading ? "loading" : "idle";
        home_refresh_spinner.Spinning = isLoading;
    }

    private void RegisterApplicationActions()
    {
        var preferencesAction = SimpleAction.New("preferences", null);
        preferencesAction.OnActivate += (_, _) =>
        {
            var preferencesDialogWrapper = new PreferencesDialog(_services.Preferences);
            preferencesDialogWrapper.Widget.Present(Widget);
        };
        Widget.AddAction(preferencesAction);

        var aboutAction = SimpleAction.New("about", null);
        aboutAction.OnActivate += (_, _) => PresentAboutDialog();
        Widget.AddAction(aboutAction);

        var quitAction = SimpleAction.New("quit", null);
        quitAction.OnActivate += (_, _) => Widget.Close();
        Widget.AddAction(quitAction);
    }

    private void PresentAboutDialog()
    {
        var dialog = AboutDialog.New();
        dialog.ApplicationName = ApplicationMetadata.ApplicationName;
        dialog.Version = ApplicationMetadata.Version;
        dialog.DeveloperName = ApplicationMetadata.DeveloperName;
        dialog.Developers = [ApplicationMetadata.DeveloperName];
        dialog.Comments = "A GTK 4 and Libadwaita desktop app for YouTube.";
        dialog.Copyright = ApplicationMetadata.Copyright;
        dialog.LicenseType = License.Gpl30Only;
        dialog.Website = ApplicationMetadata.SourceUrl;
        dialog.IssueUrl = ApplicationMetadata.IssueUrl;
        dialog.Present(Widget);
    }


    private void OnQueueStateChanged(object? sender, QueuePresentationState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_closed)
                UpdateQueueButton(state);

            return false;
        });
    }

    private void UpdateQueueButton(QueuePresentationState state)
    {
        var hasItems = state.Items.Count > 0;
        queue_button.Visible = hasItems;
        queue_button.Active = hasItems && queue_button.Active;

        if (hasItems)
            queue_button_label.SetText(state.Items.Count.ToString());
    }

    private void CloseQueue()
    {
        queue_button.Active = false;
    }

    private void OpenWebLogin()
    {
        account_button.Popover?.Popdown();
        if (_webLogin is not null)
        {
            _webLogin.Present();
            return;
        }

        _webLogin = new WebLoginWindow(Widget, _accountViewModel, () => _webLogin = null);
        _webLogin.Present();
    }

    private void UpdateAccountAppearance(bool hasManualSession, string displayName, Texture? avatar)
    {
        account_button.TooltipText = hasManualSession ? "YouTube session active" : "Account";
        account_avatar.Text = hasManualSession ? displayName : string.Empty;
        account_avatar.ShowInitials = hasManualSession;
        account_avatar.CustomImage = avatar!;
    }

    private bool OnCloseRequest(Window sender, EventArgs args)
    {
        if (_closed) return false;
        _closed = true;
        _channel.RefreshLoadingChanged -= OnChannelRefreshLoadingChanged;
        _history.RefreshLoadingChanged -= OnHistoryRefreshLoadingChanged;
        _searchView.RefreshLoadingChanged -= OnSearchRefreshLoadingChanged;
        _subscriptions.RefreshLoadingChanged -= OnSubscriptionsRefreshLoadingChanged;
        _subscriptions.Dispose();
        _subscriptionsViewModel.Dispose();
        _searchView.Dispose();
        _searchPopover.Dispose();
        _searchViewModel.Dispose();
        _history.Dispose();
        _historyViewModel.Dispose();
        _channel.Dispose();
        _channelViewModel.Dispose();

        _queueViewModel.StateChanged -= OnQueueStateChanged;
        _home.RefreshLoadingChanged -= OnHomeRefreshLoadingChanged;
        _home.Dispose();
        _queueView.Dispose();
        _webLogin?.Dispose();
        _webLogin = null;
        _accountPopover.Dispose();
        _embeddedPlayer.Dispose();
        _disposeApplicationServices();
        Dispose();

        return false;
    }
}