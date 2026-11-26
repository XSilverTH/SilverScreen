using Gdk;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using ApplicationWindow = Adw.ApplicationWindow;
using Constants = Gdk.Constants;
using PreferencesDialog = SilverScreen.Preferences.PreferencesDialog;
using Task = System.Threading.Tasks.Task;
using Window = Gtk.Window;

namespace SilverScreen.Shell;

public partial class MainWindow : WindowBase<ApplicationWindow>
{
    private static readonly ILogger Logger = Log.ForContext<MainWindow>();
    private readonly AccountServices _account;
    private readonly Action _disposeApplicationServices;
    private readonly LazyEmbeddedPlayerPresenter _embeddedPlayerPresenter;
    private readonly ShellNavigationHostView _navigationHost;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly Lazy<PlayerDependencies> _playerDependencies;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly ShellHeaderBarView _shellHeaderBar;
    private bool _closed;
    private EmbeddedPlayerView? _embeddedPlayer;

    public MainWindow(
        BrowsingServices browsing, AccountServices account, IPlaybackService playback,
        PlayerDependencies player, RuntimeDependencyDiagnostics diagnostics,
        INavigationService navigationService, INotificationService notificationService,
        SearchViewModel searchViewModel, AccountViewModel accountViewModel,
        IShellRefreshCoordinator refreshCoordinator, Action disposeApplicationServices,
        string? initialQuery = null)
        : this(
            browsing,
            account,
            new Lazy<IPlaybackService>(() => playback ?? throw new ArgumentNullException(nameof(playback))),
            new Lazy<PlayerDependencies>(() => player ?? throw new ArgumentNullException(nameof(player))),
            diagnostics,
            navigationService,
            notificationService,
            searchViewModel,
            accountViewModel,
            refreshCoordinator,
            disposeApplicationServices,
            initialQuery)
    {
    }

    public MainWindow(
        BrowsingServices browsing, AccountServices account, Lazy<IPlaybackService> playback,
        Lazy<PlayerDependencies> player, RuntimeDependencyDiagnostics diagnostics,
        INavigationService navigationService, INotificationService notificationService,
        SearchViewModel searchViewModel, AccountViewModel accountViewModel,
        IShellRefreshCoordinator refreshCoordinator, Action disposeApplicationServices,
        string? initialQuery = null)
    {
        Logger.Debug("Initializing MainWindow");
        ArgumentNullException.ThrowIfNull(playback);
        (_account, _disposeApplicationServices, _navigationService, _notificationService, _playerDependencies) = (
            account,
            disposeApplicationServices,
            navigationService,
            notificationService,
            player ?? throw new ArgumentNullException(nameof(player)));
        if (_notificationService is NotificationService notify) notify.AttachToastOverlay(toast_overlay);

        _embeddedPlayerPresenter = new LazyEmbeddedPlayerPresenter(this);
        var playback1 = new PlaybackModeRoutingService(
            account.Preferences,
            new LazyPlaybackService(playback),
            _embeddedPlayerPresenter);

        _queueViewModel = new QueueViewModel(account.Queue, playback1);
        _queueView = new QueueView(_queueViewModel, browsing.Thumbnails);
        _queueView.CloseRequested += (_, _) => queue_split_view.ShowSidebar = false;
        _queueView.PlayFailed += (_, err) => _notificationService.ShowToast(err);
        queue_sidebar_host.Append(_queueView.Widget);

        _shellHeaderBar = new ShellHeaderBarView(_navigationService, playback1, account, accountViewModel,
            searchViewModel, refreshCoordinator, diagnostics, _notificationService, _queueViewModel,
            browsing.Thumbnails);
        toolbar_view.AddTopBar(_shellHeaderBar.Widget);
        _shellHeaderBar.QueueButton.BindToSplitView(queue_split_view);

        _navigationHost = new ShellNavigationHostView(browsing, account, _navigationService, playback1,
            _notificationService, searchViewModel, refreshCoordinator, initialQuery);
        navigation_host.Append(_navigationHost.Widget);

        _shellHeaderBar.ViewSwitcher.Stack = view_switcher_bar.Stack = _navigationHost.ViewStack;
        breakpoint.OnApply += (_, _) => _shellHeaderBar.SetTitleWidgetRevealed(false);
        breakpoint.OnUnapply += (_, _) => _shellHeaderBar.SetTitleWidgetRevealed(true);

        var prefs = account.Preferences.GetPreferences();
        if (prefs is { WindowWidth: > 0, WindowHeight: > 0 })
            Widget.SetDefaultSize(prefs.WindowWidth, prefs.WindowHeight);
        if (prefs.WindowMaximized) Widget.Maximize();

        SetupDesktopBackShortcuts();
        if (_navigationService is NavigationService concreteNav)
        {
            concreteNav.AttachMainStack(main_stack);
            concreteNav.Initialize();
        }

        Lifetime.Track(() => _navigationService.PageChanged += OnNavigationPageChanged,
            () => _navigationService.PageChanged -= OnNavigationPageChanged);
        RegisterApplicationActions();
        Lifetime.Track(() => Widget.OnCloseRequest += OnCloseRequest, () => Widget.OnCloseRequest -= OnCloseRequest);
    }

    private EmbeddedPlayerView EnsureEmbeddedPlayer()
    {
        if (_embeddedPlayer is not null)
            return _embeddedPlayer;

        App.EnsurePlayerStylesInstalled();
        var embedded = new EmbeddedPlayerView(_navigationService, _playerDependencies.Value);
        _embeddedPlayer = embedded;
        _embeddedPlayerPresenter.Attach(embedded);
        player_host.Append(embedded.Widget);
        return embedded;
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

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        return Widget.GetFocus() is not (Editable or TextView) &&
               (((args.State & ModifierType.AltMask) != 0 && args.Keyval == Constants.KEY_Left) ||
                (args.Keyval == Constants.KEY_Escape && _navigationService.CurrentPage != NavigationPage.Player)) &&
               _navigationService.CanGoBack && _navigationService.GoBack();
    }

    private void OnMousePressed(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (sender.GetCurrentButton() != 8 || !_navigationService.CanGoBack) return;
        _navigationService.GoBack();
        sender.SetState(EventSequenceState.Claimed);
    }

    private void OnNavigationPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        if (_closed) return;
        if (e.CurrentPage == NavigationPage.Player)
        {
            EnsureEmbeddedPlayer();
            if (_account.Preferences.GetPreferences().OpenInFullscreen)
                Widget.Fullscreen();
        }
        else if (e.PreviousPage == NavigationPage.Player)
        {
            Widget.Unfullscreen();
        }
    }

    private void RegisterApplicationActions()
    {
        Add("preferences", () =>
        {
            var d = new PreferencesDialog(_account.Preferences);
            d.SaveFailed += (_, msg) => _notificationService.ShowToast(msg);
            d.Widget.Present(Widget);
        });
        Add("about", () => ApplicationMetadata.PresentAboutDialog(Widget));
        Add("quit", Widget.Close);
        return;

        void Add(string name, Action act)
        {
            var a = Lifetime.Own(SimpleAction.New(name, null));
            a.OnActivate += (_, _) => act();
            Widget.AddAction(a);
        }
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
            _shellHeaderBar.Dispose();
            _navigationHost.Dispose();
            _queueView.Dispose();
            _queueViewModel.Dispose();
            _embeddedPlayerPresenter.Detach();
            _embeddedPlayer?.Dispose();
            _embeddedPlayer = null;
            _disposeApplicationServices();
        }

        base.Dispose(disposing);
    }

    private sealed class LazyPlaybackService(Lazy<IPlaybackService> inner) : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request)
        {
            return inner.Value.PlayAsync(request);
        }

        public Task<string> PlayAlternateAsync(PlaybackRequest request)
        {
            return inner.Value.PlayAlternateAsync(request);
        }
    }

    private sealed class LazyEmbeddedPlayerPresenter(MainWindow owner) : IEmbeddedPlayerPresenter
    {
        private EmbeddedPlayerView? _attached;
        private EventHandler? _playbackStateChanged;

        public bool HasMedia => _attached?.HasMedia ?? false;
        public bool IsPaused => _attached?.IsPaused ?? true;

        public event EventHandler? PlaybackStateChanged
        {
            add => _playbackStateChanged += value;
            remove => _playbackStateChanged -= value;
        }

        public Task<string> PresentAsync(PlaybackRequest request)
        {
            if (_attached is not null || GLib.MainContext.Default().IsOwner())
                return owner.EnsureEmbeddedPlayer().PresentAsync(request);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            GLib.Functions.IdleAdd(0, () =>
            {
                if (owner._closed)
                {
                    tcs.TrySetResult("Window closed.");
                    return false;
                }

                try
                {
                    var task = owner.EnsureEmbeddedPlayer().PresentAsync(request);
                    task.ContinueWith(completed =>
                    {
                        if (completed.IsFaulted)
                            tcs.TrySetException(completed.Exception!.InnerExceptions);
                        else if (completed.IsCanceled)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetResult(completed.Result);
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }

                return false;
            });
            return tcs.Task;
        }

        public Task TogglePauseAsync()
        {
            return _attached is not null ? _attached.TogglePauseAsync() : Task.CompletedTask;
        }

        public void Attach(EmbeddedPlayerView embeddedPlayer)
        {
            if (ReferenceEquals(_attached, embeddedPlayer))
                return;

            _attached = embeddedPlayer;
            embeddedPlayer.PlaybackStateChanged += OnInnerPlaybackStateChanged;
        }

        public void Detach()
        {
            if (_attached is null)
                return;

            _attached.PlaybackStateChanged -= OnInnerPlaybackStateChanged;
            _attached = null;
            _playbackStateChanged = null;
        }

        private void OnInnerPlaybackStateChanged(object? sender, EventArgs e)
        {
            _playbackStateChanged?.Invoke(sender, e);
        }
    }
}