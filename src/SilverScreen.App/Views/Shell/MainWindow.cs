using System.ComponentModel;
using Adw;
using Gdk;
using Gio;
using GObject;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.ViewModels;
using SilverScreen.Views.Account;
using SilverScreen.Views.Components;
using SilverScreen.Views.Channel;
using SilverScreen.Views.Home;
using SilverScreen.Views.History;
using SilverScreen.Views.Player;
using SilverScreen.Views.Popovers;
using SilverScreen.Views.Queue;
using SilverScreen.Views.Search;
using XSTH.Blueprint.Helpers;
using AboutDialog = Adw.AboutDialog;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using Functions = GLib.Functions;
using License = Gtk.License;
using Spinner = Gtk.Spinner;
using PreferencesDialog = SilverScreen.Views.Preferences.PreferencesDialog;
using Window = Gtk.Window;
using Serilog;

namespace SilverScreen.Views.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();
    private readonly Avatar _accountAvatar;
    private readonly MenuButton _accountButton;
    private readonly AccountPopoverView _accountPopover;
    private readonly AccountViewModel _accountViewModel;
    private readonly Action _disposeApplicationServices;
    private readonly ChannelView _channel;
    private readonly ChannelViewModel _channelViewModel;
    private readonly EmbeddedPlayerView _embeddedPlayer;
    private readonly HistoryView _history;
    private readonly HistoryViewModel _historyViewModel;
    private readonly HomeView _home;
    private readonly Button _homeRefreshButton;
    private readonly Spinner _homeRefreshSpinner;
    private readonly Stack _mainStack;
    private readonly Stack _homeRefreshStack;
    private readonly Button _navigationBackButton;
    private readonly PlaybackModeRoutingService _playback;
    private readonly ToggleButton _queueButton;
    private readonly Label _queueButtonLabel;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;

    private readonly SearchPopoverView _searchPopover;
    private readonly SearchView _searchView;
    private readonly SearchViewModel _searchViewModel;
    private readonly ApplicationServices _services;
    private readonly ShellViewModel _shell = new();
    private readonly ViewStack _stack;
    private readonly Label _statusLabel;
    private bool _closed;
    private WebLoginWindow? _webLogin;

    public MainWindow(ApplicationServices services, Action disposeApplicationServices)
    {
        Logger.Information("Initializing MainWindow");
        _services = services;
        _disposeApplicationServices = disposeApplicationServices;
        _stack = GetRequiredObject<ViewStack>("view_stack");
        _mainStack = GetRequiredObject<Stack>("main_stack");
        var switcher = GetRequiredObject<ViewSwitcher>("view_switcher");
        _navigationBackButton = GetRequiredObject<Button>("navigation_back_button");
        GetRequiredObject<MenuButton>("search_button");
        var searchPopover = GetRequiredObject<Popover>("search_popover");
        _homeRefreshButton = GetRequiredObject<Button>("home_refresh_button");
        _homeRefreshSpinner = GetRequiredObject<Spinner>("home_refresh_spinner");
        _homeRefreshStack = GetRequiredObject<Stack>("home_refresh_stack");
        _accountButton = GetRequiredObject<MenuButton>("account_button");
        _accountAvatar = GetRequiredObject<Avatar>("account_avatar");
        GetRequiredObject<MenuButton>("app_menu_button");
        _queueButton = GetRequiredObject<ToggleButton>("queue_button");
        _queueButtonLabel = GetRequiredObject<Label>("queue_button_label");
        var queueSplitView = GetRequiredObject<OverlaySplitView>("queue_split_view");
        var queueSidebarHost = GetRequiredObject<Box>("queue_sidebar_host");
        var playerHost = GetRequiredObject<Box>("player_host");
        _statusLabel = GetRequiredObject<Label>("status_label");
        var accountPopover = GetRequiredObject<Popover>("account_popover");

        _embeddedPlayer = new EmbeddedPlayerView(OpenEmbeddedPlayer, CloseEmbeddedPlayer,
            video => OpenChannelAsync(video).FireAndForget(Logger), services.Preferences,
            services.CookieFiles, services.PlaybackPresence, services.PlaybackTelemetry, services.WatchProgress,
            services.VideoEngagement, services.YouTubeRating, services.SponsorBlock, services.Session, services.Comments,
            services.VideoDetails);
        _playback = new PlaybackModeRoutingService(services.Preferences, services.Playback, _embeddedPlayer);
        playerHost.Append(_embeddedPlayer.Widget);
        var actions = CreateVideoActions();
        _channelViewModel = new ChannelViewModel(services.Channels, _shell);
        _channel = new ChannelView(_channelViewModel, services.Thumbnails, services.WatchProgress, actions, CloseChannel);
        _channel.RefreshLoadingChanged += OnChannelRefreshLoadingChanged;
        _home = new HomeView(
            new HomeViewModel(services.HomeFeed),
            services.Thumbnails,
            services.WatchProgress,
            actions);
        _home.RefreshLoadingChanged += OnHomeRefreshLoadingChanged;
        _historyViewModel = new HistoryViewModel(services.History, _shell);
        _history = new HistoryView(_historyViewModel, services.Thumbnails, services.WatchProgress, actions);
        _history.RefreshLoadingChanged += OnHistoryRefreshLoadingChanged;
        var historyHost = GetRequiredObject<Box>("history_host");
        historyHost.Append(_history.Widget);
        var homeHost = GetRequiredObject<Box>("home_host");
        homeHost.Append(_home.Widget);
        UpdateHomeRefreshButton(_home.IsLoading);

        _searchViewModel = new SearchViewModel(services.Search, _playback, _shell, services.SearchSuggestions);
        _searchPopover = new SearchPopoverView(_searchViewModel, OnSearchSubmitted, () => searchPopover.Popdown());
        searchPopover.Child = _searchPopover.Widget;
        searchPopover.OnClosed += (_, _) => _searchPopover.OnClosed();
        searchPopover.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "visible" && searchPopover.GetVisible())
                _searchPopover.OnOpened();
        };

        _searchView = new SearchView(_searchViewModel, services.Thumbnails, services.WatchProgress, actions);
        _searchView.RefreshLoadingChanged += OnSearchRefreshLoadingChanged;

        _queueViewModel = new QueueViewModel(services.Queue, _playback, _shell);
        _queueView = new QueueView(_queueViewModel, services.Thumbnails, services.WatchProgress, CloseQueue);
        queueSidebarHost.Append(_queueView.Widget);
        _accountViewModel = new AccountViewModel(services.AccountProfile, services.Session, services.SessionValidation,
            _shell);
        _accountPopover = new AccountPopoverView(
            _accountViewModel,
            services.Thumbnails,
            OpenWebLogin,
            UpdateAccountAppearance);

        switcher.Stack = _stack;
        var channelPage = _stack.AddTitled(_channel.Widget, "channel", "Channel");
        channelPage.Visible = false;
        var searchPage = _stack.AddTitled(_searchView.Widget, "search", "Search");
        searchPage.Visible = false;
        _stack.VisibleChildName = _shell.SelectedPage;

        accountPopover.Child = _accountPopover.Widget;
        _queueButton.BindProperty("active", queueSplitView, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        RegisterApplicationActions();
        _shell.PropertyChanged += OnShellPropertyChanged;
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
                _shell.Status = await _playback.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false),
            OpenInAlternatePlayerAsync = OpenInAlternatePlayerAsync,
            AddToQueue = video =>
            {
                _services.Queue.Add(video);
                _shell.Status = "Video added to queue.";
            },
            ReportStatus = message => _shell.Status = message,
            OpenChannelAsync = OpenChannelAsync
        };
    }

    private async System.Threading.Tasks.Task OpenInAlternatePlayerAsync(VideoSummary video)
    {
        var request = new PlaybackRequest([video]);
        var playbackBackend = _services.Preferences.GetPreferences().PlaybackBackend;
        _shell.Status = playbackBackend == PlaybackBackends.EmbeddedPlayer
            ? await _services.Playback.PlayAsync(request).ConfigureAwait(false)
            : await _embeddedPlayer.PresentAsync(request).ConfigureAwait(false);
    }
    private async System.Threading.Tasks.Task OpenChannelAsync(VideoSummary video)
    {
        if (string.IsNullOrWhiteSpace(video.ChannelUrl))
        {
            _shell.Status = $"A channel link is not available for {video.ChannelName}.";
            return;
        }

        _stack.VisibleChildName = "channel";
        UpdateHomeRefreshButton(_channel.IsLoading);
        await _channelViewModel.OpenChannelAsync(video.ChannelUrl, video.ChannelName).ConfigureAwait(false);
    }

    private void CloseChannel()
    {
        _channelViewModel.Clear();
        _stack.VisibleChildName = "home";
        _navigationBackButton.Visible = false;
        UpdateHomeRefreshButton(_home.IsLoading);
    }
    private void OnSearchSubmitted(string query)
    {
        _stack.VisibleChildName = "search";
        _navigationBackButton.Visible = true;
        UpdateHomeRefreshButton(_searchView.IsLoading);
        _searchViewModel.SubmitAsync(query).FireAndForget(Logger);
    }

    private void CloseSearch()
    {
        _searchViewModel.Reset();
        _stack.VisibleChildName = "home";
        _navigationBackButton.Visible = false;
        UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnNavigationBackButtonClicked(object? sender = null, EventArgs? args = null)
    {
        if (_stack.VisibleChildName == "search")
            CloseSearch();
        else if (_stack.VisibleChildName == "channel")
            CloseChannel();
        else
        {
            _stack.VisibleChildName = "home";
            _navigationBackButton.Visible = false;
        }
    }
    private void OpenEmbeddedPlayer()
    {
        _mainStack.VisibleChildName = "player";
        if (_services.Preferences.GetPreferences().OpenInFullscreen)
            Widget.Fullscreen();
    }

    private void CloseEmbeddedPlayer()
    {
        Widget.Unfullscreen();
        _mainStack.VisibleChildName = "shell";
    }

    private void ReportStartupDependencyWarnings()
    {
        var warnings = _services.RuntimeDependencyDiagnostics.GetStartupWarnings();
        if (warnings.Count == 0)
            return;

        _shell.Status = $"Runtime setup needed: {string.Join(" ", warnings)}";
    }

    private void OnHomeRefreshButtonClicked(object? sender, EventArgs args)
    {
        if (_stack.VisibleChildName == "channel")
            _channel.RefreshAsync().FireAndForget(Logger);
        else if (_stack.VisibleChildName == "history")
            _history.RefreshAsync().FireAndForget(Logger);
        else if (_stack.VisibleChildName == "search")
            _searchView.RefreshAsync().FireAndForget(Logger);
        else
            _home.RefreshAsync().FireAndForget(Logger);
    }

    private void OnHomeRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _stack.VisibleChildName != "channel" && _stack.VisibleChildName != "history" && _stack.VisibleChildName != "search")
            UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnChannelRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _stack.VisibleChildName == "channel")
            UpdateHomeRefreshButton(_channel.IsLoading);
    }

    private void OnHistoryRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _stack.VisibleChildName == "history")
            UpdateHomeRefreshButton(_history.IsLoading);
    }

    private void OnSearchRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _stack.VisibleChildName == "search")
            UpdateHomeRefreshButton(_searchView.IsLoading);
    }

    private void OnViewStackNotify(object? sender = null, EventArgs? args = null)
    {
        if (_closed) return;
        _navigationBackButton.Visible = _stack.VisibleChildName is "search" or "channel";

        if (_stack.VisibleChildName == "channel")
        {
            UpdateHomeRefreshButton(_channel.IsLoading);
        }
        else if (_stack.VisibleChildName == "history")
        {
            UpdateHomeRefreshButton(_history.IsLoading);
            _historyViewModel.LoadAsync().FireAndForget(Logger);
        }
        else if (_stack.VisibleChildName == "search")
        {
            UpdateHomeRefreshButton(_searchView.IsLoading);
        }
        else
        {
            UpdateHomeRefreshButton(_home.IsLoading);
        }
    }
    private void UpdateHomeRefreshButton(bool isLoading)
    {
        _homeRefreshButton.Sensitive = !isLoading;
        _homeRefreshStack.VisibleChildName = isLoading ? "loading" : "idle";
        _homeRefreshSpinner.Spinning = isLoading;
    }

    private void RegisterApplicationActions()
    {
        var preferencesAction = SimpleAction.New("preferences", null);
        preferencesAction.OnActivate += (_, _) =>
        {
            var preferencesDialogWrapper = new PreferencesDialog(_services.Preferences,
                message => _shell.Status = message);
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

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_closed)
                return false;

            switch (args.PropertyName)
            {
                case nameof(ShellViewModel.Status):
                    _statusLabel.SetText(_shell.Status);
                    _statusLabel.TooltipText = _shell.Status;
                    break;
                case nameof(ShellViewModel.SelectedPage):
                    _stack.VisibleChildName = _shell.SelectedPage;
                    break;
            }

            return false;
        });
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
        _queueButton.Visible = hasItems;
        _queueButton.Active = hasItems && _queueButton.Active;

        if (hasItems)
            _queueButtonLabel.SetText(state.Items.Count.ToString());
    }

    private void CloseQueue()
    {
        _queueButton.Active = false;
    }

    private void OpenWebLogin()
    {
        _accountButton.Popover?.Popdown();
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
        _accountButton.TooltipText = hasManualSession ? "YouTube session active" : "Account";
        _accountAvatar.Text = hasManualSession ? displayName : string.Empty;
        _accountAvatar.ShowInitials = hasManualSession;
        _accountAvatar.CustomImage = avatar!;
    }

    private bool OnCloseRequest(Window sender, EventArgs args)
    {
        if (_closed) return false;
        _closed = true;
        _channel.RefreshLoadingChanged -= OnChannelRefreshLoadingChanged;
        _history.RefreshLoadingChanged -= OnHistoryRefreshLoadingChanged;
        _searchView.RefreshLoadingChanged -= OnSearchRefreshLoadingChanged;
        _searchView.Dispose();
        _searchPopover.Dispose();
        _searchViewModel.Dispose();
        _history.Dispose();
        _historyViewModel.Dispose();
        _channel.Dispose();
        _channelViewModel.Dispose();
        _shell.PropertyChanged -= OnShellPropertyChanged;
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
