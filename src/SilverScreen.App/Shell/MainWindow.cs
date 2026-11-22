using Adw;
using Gdk;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Player;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using PreferencesDialog = SilverScreen.Preferences.PreferencesDialog;
using Window = Gtk.Window;

namespace SilverScreen.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();
    private readonly AccountServices _account;
    private readonly Action _disposeApplicationServices;
    private readonly EmbeddedPlayerView _embeddedPlayer;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly PlaybackModeRoutingService _playback;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly ShellHeaderBarView _shellHeaderBar;
    private readonly ShellNavigationHostView _navigationHost;
    private bool _closed;

    public MainWindow(
        BrowsingServices browsing, AccountServices account, IPlaybackService playback,
        PlayerDependencies player, RuntimeDependencyDiagnostics diagnostics,
        INavigationService navigationService, INotificationService notificationService,
        SearchViewModel searchViewModel, AccountViewModel accountViewModel,
        IShellRefreshCoordinator refreshCoordinator, Action disposeApplicationServices,
        string? initialQuery = null)
    {
        Logger.Debug("Initializing MainWindow");
        (_account, _disposeApplicationServices, _navigationService, _notificationService) = (account, disposeApplicationServices, navigationService, notificationService);
        if (_notificationService is NotificationService notify) notify.AttachToastOverlay(toast_overlay);

        _embeddedPlayer = new EmbeddedPlayerView(_navigationService, player);
        _playback = new PlaybackModeRoutingService(account.Preferences, playback, _embeddedPlayer);
        player_host.Append(_embeddedPlayer.Widget);

        _queueViewModel = new QueueViewModel(account.Queue, _playback);
        _queueView = new QueueView(_queueViewModel, browsing.Thumbnails);
        _queueView.CloseRequested += (_, _) => queue_split_view.ShowSidebar = false;
        _queueView.PlayFailed += (_, err) => _notificationService.ShowToast(err);
        queue_sidebar_host.Append(_queueView.Widget);

        _shellHeaderBar = new ShellHeaderBarView(_navigationService, _playback, account, accountViewModel, searchViewModel, refreshCoordinator, diagnostics, _notificationService, _queueViewModel, browsing.Thumbnails);
        toolbar_view.AddTopBar(_shellHeaderBar.Widget);
        _shellHeaderBar.QueueButton.BindToSplitView(queue_split_view);

        _navigationHost = new ShellNavigationHostView(browsing, account, _navigationService, _playback, _notificationService, searchViewModel, refreshCoordinator, initialQuery);
        navigation_host.Append(_navigationHost.Widget);

        _shellHeaderBar.ViewSwitcher.Stack = view_switcher_bar.Stack = _navigationHost.ViewStack;
        breakpoint.OnApply += (_, _) => _shellHeaderBar.SetTitleWidgetRevealed(false);
        breakpoint.OnUnapply += (_, _) => _shellHeaderBar.SetTitleWidgetRevealed(true);

        var prefs = account.Preferences.GetPreferences();
        if (prefs is { WindowWidth: > 0, WindowHeight: > 0 }) Widget.SetDefaultSize(prefs.WindowWidth, prefs.WindowHeight);
        if (prefs.WindowMaximized) Widget.Maximize();

        SetupDesktopBackShortcuts();
        if (_navigationService is NavigationService concreteNav) { concreteNav.AttachMainStack(main_stack); concreteNav.Initialize(); }

        Lifetime.Track(() => _navigationService.PageChanged += OnNavigationPageChanged, () => _navigationService.PageChanged -= OnNavigationPageChanged);
        RegisterApplicationActions();
        Lifetime.Track(() => Widget.OnCloseRequest += OnCloseRequest, () => Widget.OnCloseRequest -= OnCloseRequest);
    }

    private void SetupDesktopBackShortcuts()
    {
        var key = EventControllerKey.New();
        key.SetPropagationPhase(PropagationPhase.Bubble);
        Lifetime.Attach(Widget, key, c => c.OnKeyPressed += OnKeyPressed, c => c.OnKeyPressed -= OnKeyPressed);

        var mouse = GestureClick.New();
        mouse.SetButton(0);
        Lifetime.Attach(Widget, mouse, c => c.OnPressed += OnMousePressed, c => c.OnPressed -= OnMousePressed);
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args) =>
        Widget.GetFocus() is not (Editable or TextView) &&
        (((args.State & ModifierType.AltMask) != 0 && args.Keyval == Gdk.Constants.KEY_Left) ||
         (args.Keyval == Gdk.Constants.KEY_Escape && _navigationService.CurrentPage != NavigationPage.Player)) &&
        _navigationService.CanGoBack && _navigationService.GoBack();

    private void OnMousePressed(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (sender.GetCurrentButton() == 8 && _navigationService.CanGoBack) { _navigationService.GoBack(); sender.SetState(EventSequenceState.Claimed); }
    }

    private void OnNavigationPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        if (_closed) return;
        if (e.CurrentPage == NavigationPage.Player && _account.Preferences.GetPreferences().OpenInFullscreen) Widget.Fullscreen();
        else if (e.PreviousPage == NavigationPage.Player) Widget.Unfullscreen();
    }

    private void RegisterApplicationActions()
    {
        void Add(string name, Action act) { var a = Lifetime.Own(SimpleAction.New(name, null)); a.OnActivate += (_, _) => act(); Widget.AddAction(a); }
        Add("preferences", () => { var d = new PreferencesDialog(_account.Preferences); d.SaveFailed += (_, msg) => _notificationService.ShowToast(msg); d.Widget.Present(Widget); });
        Add("about", () => ApplicationMetadata.PresentAboutDialog(Widget));
        Add("quit", Widget.Close);
    }

    private bool OnCloseRequest(Window sender, EventArgs args)
    {
        if (_closed) return false;
        _closed = true;
        var p = _account.Preferences.GetPreferences();
        p.WindowMaximized = Widget.Maximized;
        if (!Widget.Maximized && Widget.GetWidth() is var w and > 0 && Widget.GetHeight() is var h and > 0)
            (p.WindowWidth, p.WindowHeight) = (w, h);
        _account.Preferences.SavePreferences(p);
        Dispose();
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_notificationService is NotificationService notify) notify.DetachToastOverlay(toast_overlay);
            (_navigationService as IDisposable)?.Dispose();
            _shellHeaderBar.Dispose(); _navigationHost.Dispose(); _queueView.Dispose(); _queueViewModel.Dispose(); _embeddedPlayer.Dispose();
            _disposeApplicationServices();
        }
        base.Dispose(disposing);
    }
}
