using System.ComponentModel;
using Adw;
using Gdk;
using Gio;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;
using AboutDialog = Adw.AboutDialog;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using Constants = Gdk.Constants;
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
    private readonly NavigationService _navigationService;
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
        _channel = new ChannelView(_channelViewModel, services.Thumbnails, actions);
        _channel.RefreshLoadingChanged += OnChannelRefreshLoadingChanged;
        channel_host.Append(_channel.Widget);
        _ = services.HomeFeed.GetVideoListSource(OpenWebLogin);
        _home = new VideoListView(
            services.HomeFeed,
            services.Thumbnails,
            actions);
        _home.RefreshLoadingChanged += OnHomeRefreshLoadingChanged;
        _historyViewModel = new HistoryViewModel(services.History, services.Session, OpenWebLogin);
        _history = new VideoListView(_historyViewModel, services.Thumbnails, actions);
        _history.RefreshLoadingChanged += OnHistoryRefreshLoadingChanged;
        _subscriptionsViewModel = new SubscriptionsViewModel(
            services.Subscriptions,
            services.Channels,
            services.Session,
            true);
        _subscriptions = new SubscriptionsView(
            _subscriptionsViewModel,
            services.Thumbnails,
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
        _searchViewModel.OpenChannelRequested = channelTarget =>
            OpenChannelAsync(new VideoSummary("", "", channelTarget, TimeSpan.Zero, "", false, "", null, null,
                channelTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? channelTarget
                    : $"https://www.youtube.com/{channelTarget.TrimStart('/')}"));
        _searchPopover = new SearchPopoverView(_searchViewModel, OnSearchSubmitted, search_popover.Popdown);
        search_popover.Child = _searchPopover.Widget;
        search_popover.OnClosed += (_, _) => _searchPopover.OnClosed();
        search_popover.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "visible" && search_popover.GetVisible())
                _searchPopover.OnOpened();
        };

        _searchView = new VideoListView(_searchViewModel, services.Thumbnails, actions);
        _searchView.RefreshLoadingChanged += OnSearchRefreshLoadingChanged;
        search_host.Append(_searchView.Widget);
        _queueViewModel = new QueueViewModel(services.Queue, _playback);
        _queueView = new QueueView(_queueViewModel, services.Thumbnails, CloseQueue);
        queue_sidebar_host.Append(_queueView.Widget);
        _queueView.PlayFailed += OnQueuePlayFailed;
        _accountViewModel = new AccountViewModel(services.AccountProfile, services.Session);
        _accountPopover = new AccountPopoverView(
            _accountViewModel,
            services.Thumbnails,
            OpenWebLogin,
            UpdateAccountAppearance);

        view_switcher_title.Stack = view_stack;
        view_switcher_bar.Stack = view_stack;
        view_switcher_title.BindProperty("title-visible", view_switcher_bar, "reveal", BindingFlags.SyncCreate);

        var startupPrefs = services.Preferences.GetPreferences();
        if (startupPrefs.WindowWidth > 0 && startupPrefs.WindowHeight > 0)
            Widget.SetDefaultSize(startupPrefs.WindowWidth, startupPrefs.WindowHeight);
        if (startupPrefs.WindowMaximized)
            Widget.Maximize();

        SetupDesktopBackShortcuts();
        _navigationService = new NavigationService(main_stack, view_stack);
        _navigationService.PageChanged += OnNavigationPageChanged;
        _navigationService.Initialize();
        account_popover.Child = _accountPopover.Widget;
        _searchViewModel.PropertyChanged += OnBackLabelChanged;
        _channelViewModel.PropertyChanged += OnBackLabelChanged;
        _playback.PlaybackStateChanged += OnPlaybackStateChanged;
        queue_button.BindProperty("active", queue_split_view, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        RegisterApplicationActions();
        _queueViewModel.StateChanged += OnQueueStateChanged;
        UpdateQueueButton(_queueViewModel.State);
        UpdateNowPlayingBar();
        Widget.OnCloseRequest += OnCloseRequest;
        ReportStartupDependencyWarnings();
    }

    private void SetupDesktopBackShortcuts()
    {
        var keyController = EventControllerKey.New();
        keyController.SetPropagationPhase(PropagationPhase.Bubble);
        keyController.OnKeyPressed += (_, args) =>
        {
            if (Widget.GetFocus() is Editable or TextView)
                return false;

            if ((args.State & ModifierType.AltMask) != 0 && args.Keyval == Constants.KEY_Left)
                if (_navigationService.CanGoBack)
                {
                    OnNavigationBackButtonClicked();
                    return true;
                }

            if (args.Keyval == Constants.KEY_Escape && _navigationService.CurrentPage != NavigationPage.Player)
                if (_navigationService.CanGoBack)
                {
                    OnNavigationBackButtonClicked();
                    return true;
                }

            return false;
        };
        Widget.AddController(keyController);

        var mouseController = GestureClick.New();
        mouseController.SetButton(0);
        mouseController.OnPressed += (sender, _) =>
        {
            if (sender.GetCurrentButton() == 8)
                if (_navigationService.CanGoBack)
                {
                    OnNavigationBackButtonClicked();
                    sender.SetState(EventSequenceState.Claimed);
                }
        };
        Widget.AddController(mouseController);
    }


    private VideoCardActions CreateVideoActions()
    {
        return new VideoCardActions
        {
            PlayAsync = PlayVideoAsync,
            OpenInAlternatePlayerAsync = OpenInAlternatePlayerAsync,
            AddToQueue = video =>
            {
                _services.Queue.Add(video);
                ShowToast($"Added “{video.Title}” to queue");
            },
            OpenChannelAsync = OpenChannelAsync
        };
    }

    private async Task PlayVideoAsync(VideoSummary video)
    {
        try
        {
            var result = await _playback.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result))
            {
                Logger.Warning("Playback reported failure for video {VideoId}: {Result}", video.Id, result);
                ShowToast(result);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start playback for video {VideoId}", video.Id);
            ShowToast($"Could not start playback for “{video.Title}”.");
        }
    }

    private async Task OpenInAlternatePlayerAsync(VideoSummary video)
    {
        try
        {
            var result = await _playback.PlayAlternateAsync(new PlaybackRequest([video])).ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result))
            {
                Logger.Warning("Alternate playback reported failure for video {VideoId}: {Result}", video.Id, result);
                ShowToast(result);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start alternate playback for video {VideoId}", video.Id);
            ShowToast($"Could not play “{video.Title}” in the alternate player.");
        }
    }

    private async Task OpenChannelAsync(VideoSummary video)
    {
        if (string.IsNullOrWhiteSpace(video.ChannelUrl) ||
            video.ChannelUrl.Contains("UC0000000000000000000000", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("Cannot open channel with missing or placeholder URL for video {VideoId} ({ChannelName})",
                video.Id, video.ChannelName);
            return;
        }

        if (_navigationService.CurrentPage == NavigationPage.Player)
            Widget.Unfullscreen();

        var channelArgs = new ChannelNavigationArgs(video.ChannelUrl, video.ChannelName);
        _navigationService.NavigateTo(NavigationPage.Channel, channelArgs);
        await _channelViewModel.OpenChannelAsync(video.ChannelUrl, video.ChannelName, _channel.GetBatchSize())
            .ConfigureAwait(false);
    }

    private void CloseChannel()
    {
        _channelViewModel.Clear();
        _navigationService.NavigateTo(NavigationPage.Home);
    }

    private void OnSearchSubmitted(string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SubmitSearchAsync(trimmed, _searchView.GetBatchSize()).FireAndForget(Logger);
            return;
        }

        if (SearchViewModel.IsDirectVideoUrl(trimmed) || SearchViewModel.IsChannelTarget(trimmed))
        {
            SubmitSearchAsync(trimmed, _searchView.GetBatchSize()).FireAndForget(Logger);
            return;
        }

        _navigationService.NavigateTo(NavigationPage.Search, trimmed);
        SubmitSearchAsync(trimmed, _searchView.GetBatchSize()).FireAndForget(Logger);
    }

    private async Task SubmitSearchAsync(string query, int batchSize)
    {
        var notice = await _searchViewModel.SubmitAsync(query, batchSize).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(notice))
            ShowToast(notice);
    }

    private void CloseSearch()
    {
        _searchViewModel.Reset();
        _navigationService.NavigateTo(NavigationPage.Home);
    }

    private void OnNavigationBackButtonClicked(object? sender = null, EventArgs? args = null)
    {
        if (!_navigationService.GoBack()) _navigationService.NavigateTo(NavigationPage.Home);
    }

    private void OpenEmbeddedPlayer()
    {
        _navigationService.NavigateTo(NavigationPage.Player);
        if (_services.Preferences.GetPreferences().OpenInFullscreen)
            Widget.Fullscreen();
    }

    private void CloseEmbeddedPlayer()
    {
        Widget.Unfullscreen();
        if (!_navigationService.GoBack()) _navigationService.NavigateTo(NavigationPage.Home);
    }

    private void ReportStartupDependencyWarnings()
    {
        var warnings = _services.RuntimeDependencyDiagnostics.GetStartupWarnings();
        if (warnings.Count == 0)
            return;

        Logger.Warning("Runtime setup needed: {Warnings}", string.Join(" ", warnings));

        var popoverContent = Box.New(Orientation.Vertical, 12);
        popoverContent.MarginTop = 16;
        popoverContent.MarginBottom = 16;
        popoverContent.MarginStart = 16;
        popoverContent.MarginEnd = 16;

        var heading = Label.New("Runtime setup needed");
        heading.AddCssClass("heading");
        heading.Xalign = 0;
        popoverContent.Append(heading);

        foreach (var warning in warnings)
        {
            var warningLabel = Label.New(warning);
            warningLabel.Wrap = true;
            warningLabel.MaxWidthChars = 48;
            warningLabel.Xalign = 0;
            popoverContent.Append(warningLabel);
        }

        var preferencesButton = Button.NewWithLabel("Open Preferences");
        preferencesButton.OnClicked += (_, _) =>
        {
            deps_popover.Popdown();
            ShowPreferences();
        };
        popoverContent.Append(preferencesButton);

        deps_popover.Child = popoverContent;
        deps_button.Visible = true;
    }

    private void OnHomeRefreshButtonClicked(object? sender, EventArgs args)
    {
        switch (_navigationService.CurrentPage)
        {
            case NavigationPage.Channel:
                _channel.RefreshAsync().FireAndForget(Logger);
                break;
            case NavigationPage.History:
                _history.RefreshAsync().FireAndForget(Logger);
                break;
            case NavigationPage.Subscriptions:
                _subscriptions.RefreshAsync().FireAndForget(Logger);
                break;
            case NavigationPage.Search:
                _searchView.RefreshAsync().FireAndForget(Logger);
                break;
            default:
                _home.RefreshAsync().FireAndForget(Logger);
                break;
        }
    }

    private void OnHomeRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _navigationService.CurrentPage == NavigationPage.Home)
            UpdateHomeRefreshButton(_home.IsLoading);
    }

    private void OnChannelRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _navigationService.CurrentPage == NavigationPage.Channel)
            UpdateHomeRefreshButton(_channel.IsLoading);
    }

    private void OnHistoryRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _navigationService.CurrentPage == NavigationPage.History)
            UpdateHomeRefreshButton(_history.IsLoading);
    }

    private void OnSubscriptionsRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _navigationService.CurrentPage == NavigationPage.Subscriptions)
            UpdateHomeRefreshButton(_subscriptions.IsLoading);
    }

    private void OnSearchRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed && _navigationService.CurrentPage == NavigationPage.Search)
            UpdateHomeRefreshButton(_searchView.IsLoading);
    }

    private void OnViewStackNotify(object? sender = null, EventArgs? args = null)
    {
        if (_closed) return;
        _navigationService.SyncFromViewStack(view_stack.VisibleChildName);
    }

    private void OnNavigationPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        if (_closed) return;
        UpdateBackButton();
        UpdateNowPlayingBar();

        if (e.IsBackNavigation)
        {
            if (e.CurrentPage == NavigationPage.Channel && e.CurrentParameter is ChannelNavigationArgs channelArgs)
                _channelViewModel.OpenChannelAsync(channelArgs.Url, channelArgs.Name ?? "Channel",
                        _channel.GetBatchSize())
                    .FireAndForget(Logger);
            else if (e.CurrentPage == NavigationPage.Search && e.CurrentParameter is string query)
                SubmitSearchAsync(query, _searchView.GetBatchSize()).FireAndForget(Logger);
        }

        if (e.PreviousPage == NavigationPage.Channel && e.CurrentPage != NavigationPage.Channel)
            _channelViewModel.Clear();

        if (e.PreviousPage == NavigationPage.Search && e.CurrentPage != NavigationPage.Search) _searchViewModel.Reset();

        var childChanged = e.CurrentPage != e.PreviousPage;
        switch (e.CurrentPage)
        {
            case NavigationPage.Channel:
                UpdateHomeRefreshButton(_channel.IsLoading);
                break;
            case NavigationPage.History:
                UpdateHomeRefreshButton(_history.IsLoading);
                if (childChanged)
                    _historyViewModel.LoadAsync(_history.GetBatchSize()).FireAndForget(Logger);
                break;
            case NavigationPage.Subscriptions:
                UpdateHomeRefreshButton(_subscriptions.IsLoading);
                if (childChanged)
                    _subscriptionsViewModel.LoadAsync(_subscriptions.GetBatchSize()).FireAndForget(Logger);
                break;
            case NavigationPage.Search:
                UpdateHomeRefreshButton(_searchView.IsLoading);
                break;
            case NavigationPage.Home:
                UpdateHomeRefreshButton(_home.IsLoading);
                break;
            case NavigationPage.Player:
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
        preferencesAction.OnActivate += (_, _) => ShowPreferences();
        Widget.AddAction(preferencesAction);

        var aboutAction = SimpleAction.New("about", null);
        aboutAction.OnActivate += (_, _) => PresentAboutDialog();
        Widget.AddAction(aboutAction);

        var quitAction = SimpleAction.New("quit", null);
        quitAction.OnActivate += (_, _) => Widget.Close();
        Widget.AddAction(quitAction);
    }

    private void ShowPreferences()
    {
        var preferencesDialogWrapper = new PreferencesDialog(_services.Preferences);
        preferencesDialogWrapper.SaveFailed += OnPreferencesSaveFailed;
        preferencesDialogWrapper.Widget.Present(Widget);
    }

    private void OnPreferencesSaveFailed(object? sender, string message)
    {
        ShowToast(message);
    }

    private void PresentAboutDialog()
    {
        var dialog = AboutDialog.New();
        dialog.ApplicationIcon = ApplicationMetadata.IconName;
        dialog.ApplicationName = ApplicationMetadata.ApplicationName;
        dialog.Version = ApplicationMetadata.Version;
        dialog.DeveloperName = ApplicationMetadata.DeveloperName;
        dialog.Developers = [ApplicationMetadata.DeveloperName];
        dialog.Comments = "A GTK 4 and Libadwaita desktop app for YouTube.";
        dialog.Copyright = ApplicationMetadata.Copyright;
        dialog.LicenseType = License.Gpl30;
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
        queue_button.Visible = true;
        queue_button_label.SetText(state.Items.Count.ToString());
    }

    private void CloseQueue()
    {
        queue_button.Active = false;
    }

    /// <summary>
    ///     Shows a transient toast for one-shot failures (play, paste, queue, save).
    ///     Kept as a plain shell method so a future notification bus can take over without touching callers.
    ///     Safe to call from any thread.
    /// </summary>
    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var text = message.Trim();
        Functions.IdleAdd(0, () =>
        {
            if (!_closed)
                toast_overlay.AddToast(Toast.New(text));

            return false;
        });
    }

    private void UpdateBackButton()
    {
        var (visible, label, tooltip) = _navigationService.CurrentPage switch
        {
            NavigationPage.Search => (true, _searchViewModel.BackLabel, "Exit Search"),
            NavigationPage.Channel => (true, _channelViewModel.BackLabel, _channelViewModel.BackTooltip),
            _ => (false, "Back", "Back")
        };

        var text = string.IsNullOrWhiteSpace(label) ? "Back" : label;
        navigation_back_button.Visible = visible;
        navigation_back_button.SetLabel(text);
        navigation_back_button.TooltipText = tooltip;
    }

    private void OnNowPlayingToggleClicked(object? sender = null, EventArgs? args = null)
    {
        _playback.TogglePauseAsync().FireAndForget(Logger);
    }

    private void OnNowPlayingReturnButtonClicked(object? sender = null, EventArgs? args = null)
    {
        OpenEmbeddedPlayer();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_closed)
                UpdateNowPlayingBar();

            return false;
        });
    }

    private void UpdateNowPlayingBar()
    {
        var hasMedia = _playback.HasMedia;
        var notInPlayer = _navigationService.CurrentPage != NavigationPage.Player;
        var show = hasMedia && notInPlayer;

        now_playing_box.Visible = show;
        if (show)
        {
            var isPaused = _playback.IsPaused;
            now_playing_toggle.IconName = isPaused
                ? "media-playback-start-symbolic"
                : "media-playback-pause-symbolic";
            now_playing_toggle.TooltipText = isPaused ? "Play" : "Pause";
        }
    }

    private void OnBackLabelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.Equals(args.PropertyName, "BackLabel", StringComparison.Ordinal) &&
            !string.Equals(args.PropertyName, "BackTooltip", StringComparison.Ordinal))
            return;
        Functions.IdleAdd(0, () =>
        {
            if (!_closed)
                UpdateBackButton();

            return false;
        });
    }

    private void OnQueuePlayFailed(object? sender, string error)
    {
        ShowToast(error);
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

        var prefs = _services.Preferences.GetPreferences();
        var isMaximized = Widget.Maximized;
        prefs.WindowMaximized = isMaximized;
        if (!isMaximized)
        {
            Widget.GetDefaultSize(out var width, out var height);
            if (width > 0 && height > 0)
            {
                prefs.WindowWidth = width;
                prefs.WindowHeight = height;
            }
        }

        _services.Preferences.SavePreferences(prefs);
        _playback.PlaybackStateChanged -= OnPlaybackStateChanged;
        _navigationService.PageChanged -= OnNavigationPageChanged;
        _navigationService.Dispose();
        _searchView.RefreshLoadingChanged -= OnSearchRefreshLoadingChanged;
        _searchViewModel.PropertyChanged -= OnBackLabelChanged;
        _channelViewModel.PropertyChanged -= OnBackLabelChanged;
        _queueView.PlayFailed -= OnQueuePlayFailed;
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