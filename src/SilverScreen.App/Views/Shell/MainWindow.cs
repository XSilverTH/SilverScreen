using System.ComponentModel;
using Adw;
using Gio;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Account;
using SilverScreen.Views.Components;
using SilverScreen.Views.Home;
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
using PreferencesWindow = SilverScreen.Views.Preferences.PreferencesWindow;
using Window = Gtk.Window;

namespace SilverScreen.Views.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();
    private readonly MenuButton _accountButton;
    private readonly AccountPopoverView _accountPopover;
    private readonly AccountViewModel _accountViewModel;
    private readonly Action _disposeApplicationServices;
    private readonly EmbeddedPlayerView _embeddedPlayer;
    private readonly HomeView _home;
    private readonly Stack _mainStack;
    private readonly IPlaybackService _playback;
    private readonly ToggleButton _queueButton;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly SearchView _search;
    private readonly Entry _searchEntry;
    private readonly Popover _searchPopover;
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
        GetRequiredObject<Button>("search_button");
        _searchPopover = GetRequiredObject<Popover>("search_popover");
        _searchEntry = GetRequiredObject<Entry>("search_entry");
        _accountButton = GetRequiredObject<MenuButton>("account_button");
        var appMenuButton = GetRequiredObject<MenuButton>("app_menu_button");
        _queueButton = GetRequiredObject<ToggleButton>("queue_button");
        var queueSplitView = GetRequiredObject<OverlaySplitView>("queue_split_view");
        var queueSidebarHost = GetRequiredObject<Box>("queue_sidebar_host");
        var playerHost = GetRequiredObject<Box>("player_host");
        _statusLabel = GetRequiredObject<Label>("status_label");

        _embeddedPlayer = new EmbeddedPlayerView(OpenEmbeddedPlayer, CloseEmbeddedPlayer, services.Preferences,
            services.CookieFiles, services.PlaybackPresence);
        _playback = new PlaybackModeRoutingService(services.Preferences, services.Playback, _embeddedPlayer);
        playerHost.Append(_embeddedPlayer.Widget);
        var actions = CreateVideoActions();
        _home = new HomeView(new HomeViewModel(services.HomeFeed), services.Thumbnails, actions);
        _search = new SearchView(new SearchViewModel(services.Search, _playback, _shell), services.Thumbnails,
            actions);
        _queueViewModel = new QueueViewModel(services.Queue, _playback, _shell);
        _queueView = new QueueView(_queueViewModel, services.Thumbnails, CloseQueue);
        queueSidebarHost.Append(_queueView.Widget);
        _accountViewModel = new AccountViewModel(services.Session, services.SessionValidation, _shell);
        _accountPopover = new AccountPopoverView(
            _accountViewModel,
            OpenWebLogin,
            UpdateAccountAppearance);

        switcher.Stack = _stack;
        _stack.AddTitled(_home.Widget, "home", "Home").IconName = "go-home-symbolic";
        _stack.AddTitled(_search.Widget, "search", "Search").IconName = "system-search-symbolic";
        _stack.VisibleChildName = _shell.SelectedPage;

        _accountButton.Popover = CreateAccountPopover();
        _queueButton.BindProperty("active", queueSplitView, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        appMenuButton.MenuModel = CreateApplicationMenuModel();
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
            AddToQueue = video =>
            {
                _services.Queue.Add(video);
                _shell.Status = "Video added to queue.";
            },
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

    private void OnSearchButtonClicked(object? sender, EventArgs args)
    {
        _searchPopover.Popup();
        _searchEntry.GrabFocus();
    }

    private async void OnSearchEntryActivated(object? sender, EventArgs args)
    {
        try
        {
            await _search.SubmitAsync(_searchEntry.GetText());
            _searchPopover.Popdown();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to submit search: {Message}", e.Message);
        }
    }

    private Popover CreateAccountPopover()
    {
        var popover = Popover.New();
        popover.Child = _accountPopover.Widget;
        return popover;
    }

    private Menu CreateApplicationMenuModel()
    {
        var preferencesAction = SimpleAction.New("preferences", null);
        preferencesAction.OnActivate += (_, _) =>
        {
            var preferencesWindowWrapper = new PreferencesWindow(_services.Preferences,
                message => _shell.Status = message);
            var preferencesWindow = preferencesWindowWrapper.Widget;
            preferencesWindow.TransientFor = Widget;
            preferencesWindow.Present();
        };
        Widget.AddAction(preferencesAction);

        var aboutAction = SimpleAction.New("about", null);
        aboutAction.OnActivate += (_, _) => PresentAboutDialog();
        Widget.AddAction(aboutAction);

        var quitAction = SimpleAction.New("quit", null);
        quitAction.OnActivate += (_, _) => Widget.Close();
        Widget.AddAction(quitAction);

        var menu = Menu.New();
        menu.Append("Preferences", "win.preferences");
        menu.Append("About SilverScreen", "win.about");
        menu.Append("Quit", "win.quit");
        return menu;
    }

    private void PresentAboutDialog()
    {
        var dialog = AboutDialog.New();
        dialog.ApplicationName = ApplicationMetadata.ApplicationName;
        dialog.Version = ApplicationMetadata.Version;
        dialog.DeveloperName = ApplicationMetadata.DeveloperName;
        dialog.Developers = [ApplicationMetadata.DeveloperName];
        dialog.Comments = "A GTK 4 and Libadwaita desktop app for finding YouTube videos and opening them in MPV.";
        dialog.Copyright = ApplicationMetadata.Copyright;
        dialog.LicenseType = License.Gpl30Only;
        dialog.Website = ApplicationMetadata.SourceUrl;
        dialog.IssueUrl = ApplicationMetadata.IssueUrl;
        // dialog.DebugInfo = ApplicationMetadata.DebugInformation;
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
        _queueButton.Child = QueueButtonContent(state.Items.Count);
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

    private void UpdateAccountAppearance(bool hasManualSession)
    {
        _accountButton.TooltipText = hasManualSession ? "YouTube session active" : "Account";
    }

    private bool OnCloseRequest(Window sender, EventArgs args)
    {
        if (_closed) return false;
        _closed = true;
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _queueViewModel.StateChanged -= OnQueueStateChanged;
        _home.Dispose();
        _search.Dispose();
        _queueView.Dispose();
        _webLogin?.Dispose();
        _webLogin = null;
        _accountPopover.Dispose();
        _embeddedPlayer.Dispose();
        _disposeApplicationServices();
        Dispose();

        return false;
    }


    private static Box QueueButtonContent(int count)
    {
        var content = Box.New(Orientation.Horizontal, 6);
        content.MarginStart = 8;
        content.MarginEnd = 8;
        content.MarginTop = 4;
        content.MarginBottom = 4;
        var icon = Image.NewFromIconName("playlist-symbolic");
        icon.PixelSize = 16;
        content.Append(icon);
        var label = Label.New(count.ToString());
        label.CssClasses = ["queue-count"];
        content.Append(label);
        return content;
    }
}