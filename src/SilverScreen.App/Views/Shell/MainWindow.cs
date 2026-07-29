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
using SilverScreen.Views.Home;
using SilverScreen.Views.Player;
using SilverScreen.Views.Popovers;
using SilverScreen.Views.Queue;
using XSTH.Blueprint.Helpers;
using AboutDialog = Adw.AboutDialog;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using Functions = GLib.Functions;
using License = Gtk.License;
using Spinner = Gtk.Spinner;
using PreferencesDialog = SilverScreen.Views.Preferences.PreferencesDialog;
using Window = Gtk.Window;

namespace SilverScreen.Views.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private readonly Avatar _accountAvatar;
    private readonly MenuButton _accountButton;
    private readonly AccountPopoverView _accountPopover;
    private readonly AccountViewModel _accountViewModel;
    private readonly Action _disposeApplicationServices;
    private readonly EmbeddedPlayerView _embeddedPlayer;
    private readonly HomeView _home;
    private readonly Button _homeRefreshButton;
    private readonly Spinner _homeRefreshSpinner;
    private readonly Stack _homeRefreshStack;
    private readonly Stack _mainStack;
    private readonly PlaybackModeRoutingService _playback;
    private readonly ToggleButton _queueButton;
    private readonly Label _queueButtonLabel;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly Button _searchButton;
    private readonly Image _searchButtonIcon;
    private readonly ApplicationServices _services;
    private readonly ShellViewModel _shell = new();
    private readonly ViewStack _stack;
    private readonly Label _statusLabel;
    private bool _closed;
    private WebLoginWindow? _webLogin;

    public MainWindow(ApplicationServices services, Action disposeApplicationServices)
    {
        _services = services;
        _disposeApplicationServices = disposeApplicationServices;
        _stack = GetRequiredObject<ViewStack>("view_stack");
        _mainStack = GetRequiredObject<Stack>("main_stack");
        var switcher = GetRequiredObject<ViewSwitcher>("view_switcher");
        _searchButton = GetRequiredObject<Button>("search_button");
        _searchButtonIcon = GetRequiredObject<Image>("search_button_icon");
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

        _embeddedPlayer = new EmbeddedPlayerView(OpenEmbeddedPlayer, CloseEmbeddedPlayer, services.Preferences,
            services.CookieFiles, services.PlaybackPresence, services.PlaybackTelemetry, services.VideoEngagement,
            services.YouTubeRating, services.SponsorBlock, services.Session, services.Comments);
        _playback = new PlaybackModeRoutingService(services.Preferences, services.Playback, _embeddedPlayer);
        playerHost.Append(_embeddedPlayer.Widget);
        var actions = CreateVideoActions();
        _home = new HomeView(
            new HomeViewModel(services.HomeFeed),
            new SearchViewModel(services.Search, _playback, _shell),
            services.Thumbnails,
            actions);
        _home.RefreshLoadingChanged += OnHomeRefreshLoadingChanged;
        _home.SearchModeChanged += OnHomeSearchModeChanged;
        UpdateHomeRefreshButton(_home.IsLoading);
        UpdateSearchButton(_home.IsSearchActive);
        _queueViewModel = new QueueViewModel(services.Queue, _playback, _shell);
        _queueView = new QueueView(_queueViewModel, services.Thumbnails, CloseQueue);
        queueSidebarHost.Append(_queueView.Widget);
        _accountViewModel = new AccountViewModel(services.AccountProfile, services.Session, services.SessionValidation,
            _shell);
        _accountPopover = new AccountPopoverView(
            _accountViewModel,
            services.Thumbnails,
            OpenWebLogin,
            UpdateAccountAppearance);

        switcher.Stack = _stack;
        _stack.AddTitled(_home.Widget, "home", "Home").IconName = "go-home-symbolic";
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
            AddNext = video =>
            {
                _services.Queue.AddNext(video);
                _shell.Status = "Video added next in queue.";
            },
            ReportStatus = message => _shell.Status = message
        };
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
        _ = _home.RefreshAsync();
    }

    private void OnHomeRefreshLoadingChanged(object? sender, bool isLoading)
    {
        if (!_closed)
            UpdateHomeRefreshButton(isLoading);
    }

    private void UpdateHomeRefreshButton(bool isLoading)
    {
        _homeRefreshButton.Sensitive = !isLoading;
        _homeRefreshStack.VisibleChildName = isLoading ? "loading" : "idle";
        _homeRefreshSpinner.Spinning = isLoading;
    }

    private void OnSearchButtonClicked(object? sender, EventArgs args)
    {
        if (_home.IsSearchActive)
            _home.ReturnToHome();
        else
            _home.ActivateSearch();
    }

    private void OnHomeSearchModeChanged(object? sender, bool isSearchActive)
    {
        if (!_closed)
            UpdateSearchButton(isSearchActive);
    }

    private void UpdateSearchButton(bool isSearchActive)
    {
        _searchButton.TooltipText = isSearchActive ? "Back to Home" : "Search";
        _searchButtonIcon.IconName = isSearchActive ? "go-previous-symbolic" : "system-search-symbolic";
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
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _home.SearchModeChanged -= OnHomeSearchModeChanged;
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