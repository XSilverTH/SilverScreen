using System.Collections.Immutable;
using System.Linq;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Player.Comments;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.RichText;
using SilverScreen.Queue;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Object = GObject.Object;
using Window = Gtk.Window;

namespace SilverScreen.Player.Views;

internal interface IEmbeddedPlayerPresenter
{
    bool HasMedia { get; }
    bool IsPaused { get; }
    Task<string> PresentAsync(PlaybackRequest request);
    event EventHandler? PlaybackStateChanged;
    Task TogglePauseAsync();
}

/// <summary>
///     Structural host view composing the player surface, overlay layers, comments, and queue sidebars.
/// </summary>
public partial class EmbeddedPlayerView : ViewBase<OverlaySplitView>, IEmbeddedPlayerPresenter
{
    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();

    private readonly INavigationService _navigation;
    private readonly IPreferencesService _preferences;
    private readonly IYouTubeMediaResolver _mediaResolver;
    private readonly IQueueService _queueService;
    private readonly LibMpvPlayer _player;
    private readonly PlaybackSession _session;
    private readonly PlayerLinkRouter _linkRouter;
    private readonly PlayerOverlayComposer _overlay;
    private readonly CommentsView _commentsView;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;

    private PlaybackRequest? _lastPlaybackRequest;
    private PlaybackRequest? _loadedRequest;
    private VideoSummary? _failedVideo;
    private bool _syncingQueue;

    public bool HasMedia => _session.HasMedia;
    public bool IsPaused { get; private set; } = true;
    public event EventHandler? PlaybackStateChanged;

    public EmbeddedPlayerView(INavigationService navigation, PlayerDependencies dependencies)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ArgumentNullException.ThrowIfNull(dependencies);
        _preferences = dependencies.Preferences;
        _mediaResolver = dependencies.MediaResolver;
        _queueService = dependencies.Queue;

        _player = Lifetime.Own(new LibMpvPlayer(a => Functions.IdleAdd(0, () => { if (!IsDisposed) a(); return false; })));
        var desktopMedia = Lifetime.Own(new DesktopMediaIntegration(_player, PresentPlayer));
        _session = Lifetime.Own(new PlaybackSession(dependencies, desktopMedia));
        _linkRouter = new PlayerLinkRouter(pos => _player.SeekAbsolute(pos), v => PresentAsync(new PlaybackRequest([v])).FireAndForget(Logger), (url, name) => _navigation.NavigateToChannel(url, name), tag => _navigation.NavigateToSearch("#" + tag), OpenExternalLink);
        _overlay = Lifetime.Own(new PlayerOverlayComposer(
            player_overlay, Widget, _player, _session, _navigation, _preferences, dependencies, _linkRouter,
            ReturnToShell, ToggleFullscreen, CloseComments, () => Widget.ShowSidebar = true, () => Widget.ShowSidebar));

        _queueViewModel = Lifetime.Own(new QueueViewModel(dependencies.Queue, new EmbeddedPlayerPlaybackService(this)));
        _queueView = Lifetime.Own(new QueueView(_queueViewModel, dependencies.Thumbnails, CloseQueue, idx =>
        {
            if (_session.Request is { } req && idx >= 0 && idx < req.Videos.Length) _player.PlayPlaylistIndex(idx);
        }));
        _commentsView = Lifetime.Own(new CommentsView(new CommentsViewModel(dependencies.Comments), CloseComments, uri => _linkRouter.Activate(uri)));

        comments_sidebar_host.Append(_commentsView.Widget);
        player_queue_sidebar_host.Append(_queueView.Widget);
        WireEvents();
    }

    private void WireEvents()
    {
        _overlay.Surface.Realized += (_, _) => OnSurfaceRealized();
        _overlay.Surface.PlaybackFailed += OnPlaybackFailed;
        _overlay.ControlsBar.QueueToggled += (_, open) => player_queue_split_view.ShowSidebar = open;
        _overlay.Error.RetryRequested += (_, _) => { if (_lastPlaybackRequest is not null) PresentAsync(_lastPlaybackRequest); };
        _overlay.Error.BrowserRequested += (_, _) => OpenBrowser();

        Lifetime.Track(() => _session.VideoChanged += OnSessionVideoChanged, () => _session.VideoChanged -= OnSessionVideoChanged);
        Lifetime.Track(() => _session.SessionEnded += OnSessionEnded, () => _session.SessionEnded -= OnSessionEnded);
        Lifetime.Track(() => _session.Failed += OnSessionFailed, () => _session.Failed -= OnSessionFailed);
        Lifetime.Track(() => _player.StateChanged += OnStateChanged, () => _player.StateChanged -= OnStateChanged);
        Lifetime.Track(
            () => _player.PlaybackFailed += OnPlaybackFailed,
            () => _player.PlaybackFailed -= OnPlaybackFailed);
        Lifetime.Track(() => _queueService.Changed += OnQueueChanged, () => _queueService.Changed -= OnQueueChanged);
        Lifetime.Track(() => _preferences.PreferencesChanged += OnPreferencesChanged, () => _preferences.PreferencesChanged -= OnPreferencesChanged);
    }

    public Task<string> PresentAsync(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Videos.Length == 0) return Task.FromResult("No videos to play.");

        try
        {
            _ = MpvCommandBuilder.GetPlaybackUrls(request);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to resolve playback URLs for request");
            return Task.FromResult(DiagnosticSanitizer.Sanitize(exception.Message));
        }

        if (!_player.IsAvailable)
            return Task.FromResult(_player.AvailabilityError ?? RuntimeDependencyGuidance.LibMpvUnavailable);

        Functions.IdleAdd(0, () =>
        {
            if (IsDisposed) return false;
            EndSession(true);
            _session.Start(request);
            _syncingQueue = true;
            try { _queueService.Replace(request.Videos); } finally { _syncingQueue = false; }
            _queueViewModel.SetCurrentPlayingIndex(0);
            var first = request.Videos[0];
            _failedVideo = null;
            _lastPlaybackRequest = request;
            _overlay.Error.ClearError();
            _overlay.Timeline.Reset();
            _overlay.Timeline.SetExpectedMedia(first.Duration, first.Duration <= TimeSpan.Zero);
            _overlay.InfoPanel.SetVideo(first);
            _overlay.Chrome.RegisterActivity();
            _overlay.ControlsBar.SetVolume(100);
            _overlay.ControlsBar.SetSpeed(1);
            _overlay.ControlsBar.LoadQualityTracks(first.Id, _mediaResolver, _preferences.GetPreferences().Quality.ToPersistedString());
            _overlay.Loading.SetLoading(true);
            _overlay.ControlsBar.SetQueueControls(request.Videos.Length > 1, false, request.Videos.Length > 1, player_queue_split_view.ShowSidebar);
            PresentPlayer();
            _overlay.Shortcuts.Attach();
            Widget.GrabFocus();

            if (!_overlay.Surface.IsRendererReady) _overlay.Surface.MakeCurrent();
            if (!_overlay.Surface.IsRendererReady) return false;
            _player.Load(request, _preferences.GetPreferences(), _session.CookieFilePath);
            _loadedRequest = request;
            return false;
        });
        return Task.FromResult("Opening embedded player.");
    }

    public Task TogglePauseAsync() { _player.TogglePause(); IsPaused = !IsPaused; return Task.CompletedTask; }
    private void PresentPlayer() { _navigation.NavigateToPlayer(); if (_preferences.GetPreferences().OpenInFullscreen && Widget.GetRoot() is Window w) w.Fullscreen(); }
    private void ReturnToShell() { _overlay.Shortcuts.Detach(); EndSession(true); if (Widget.GetRoot() is Window w) w.Unfullscreen(); if (!_navigation.GoBack()) _navigation.NavigateTo(NavigationPage.Home); }
    private bool ToggleFullscreen() { if (Widget.GetRoot() is not Window w) return false; var fs = !w.Fullscreened; if (fs) w.Fullscreen(); else w.Unfullscreen(); _overlay.HeaderBar.SetFullscreen(fs); return fs; }
    private void CloseComments() { Widget.ShowSidebar = false; if (_session.HasMedia) _overlay.Surface.GrabFocus(); }
    private void CloseQueue() { player_queue_split_view.ShowSidebar = false; if (_session.HasMedia) _overlay.Surface.GrabFocus(); }
    private void OpenBrowser() { var url = _failedVideo is { } v ? (v.WatchUrl ?? PlaybackRequest.BuildWatchUrl(v.Id)) : null; if (!string.IsNullOrWhiteSpace(url)) OpenExternalLink(url); }
    private void OpenExternalLink(string url) { try { if (Widget.GetRoot() is Window p) UriLauncher.New(url).LaunchAsync(p).FireAndForget(Logger); } catch (Exception ex) { Logger.Warning(ex, "Failed to open link"); } }

    private void OnSurfaceRealized()
    {
        _overlay.Shortcuts.Attach();
        if (_session.Request is null || ReferenceEquals(_loadedRequest, _session.Request)) return;
        _player.Load(_session.Request, _preferences.GetPreferences(), _session.CookieFilePath);
        _loadedRequest = _session.Request;
    }

    private void OnStateChanged(object? sender, LibMpvPlaybackState state)
    {
        IsPaused = state.IsPaused;
        _session.UpdatePlayback(state);
        var count = _queueService.Items.Count;
        _overlay.SyncState(state, count > 1, state.PlaylistIndex > 0, state.PlaylistIndex < count - 1, player_queue_split_view.ShowSidebar);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionVideoChanged(VideoSummary video, int playlistIndex)
    {
        _queueViewModel.SetCurrentPlayingIndex(playlistIndex);
        _overlay.SyncSessionVideo(video, _preferences.GetPreferences().Quality.ToPersistedString());
        _commentsView.SetVideo(video.Id);
        if (Widget.ShowSidebar) _commentsView.EnsureLoaded();
    }

    private void OnPlaybackFailed(object? sender, string detail) => _session.Fail(detail);

    private void OnSessionFailed(string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _failedVideo = _session.CurrentVideo ?? _loadedRequest?.Videos.FirstOrDefault() ?? _lastPlaybackRequest?.Videos.FirstOrDefault();
        _overlay.SyncSessionFailed(detail, _failedVideo);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionEnded()
    {
        _overlay.SyncSessionEnded();
        _loadedRequest = null;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _commentsView.SetVideo(null);
        CloseComments();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        Functions.IdleAdd(0, () =>
        {
            if (IsDisposed || _syncingQueue || _session.Request is null) return false;

            var currentVideos = _session.Request.Videos;
            var newItems = _queueService.Items;
            var newVideos = newItems.Select(i => i.Video).ToImmutableArray();

            if (newVideos.SequenceEqual(currentVideos)) return false;

            if (newVideos.Length > currentVideos.Length &&
                newVideos.Take(currentVideos.Length).SequenceEqual(currentVideos))
            {
                for (var i = currentVideos.Length; i < newVideos.Length; i++)
                {
                    var video = newVideos[i];
                    var url = video.WatchUrl ?? PlaybackRequest.BuildWatchUrl(video.Id);
                    if (!string.IsNullOrWhiteSpace(url))
                        _player.AppendPlaylistItem(url);
                }
            }
            else if (newVideos.Length == currentVideos.Length - 1)
            {
                var removedIndex = -1;
                for (var i = 0; i < newVideos.Length; i++)
                    if (currentVideos[i].Id != newVideos[i].Id)
                    {
                        removedIndex = i;
                        break;
                    }

                if (removedIndex < 0)
                    removedIndex = currentVideos.Length - 1;

                _player.RemovePlaylistItem(removedIndex);
            }
            else if (newVideos.Length == currentVideos.Length)
            {
                if (QueuePlaylistMove.TryTranslate(currentVideos, newVideos, out var fromIndex, out var toIndex))
                    _player.MovePlaylistItem(fromIndex, toIndex);
            }

            _session.UpdateQueue(newVideos);
            if (_session.Request is { } updatedRequest)
            {
                _player.UpdatePlaylistRequest(updatedRequest);
                _loadedRequest = updatedRequest;
            }

            var count = newVideos.Length;
            var idx = _session.CurrentPlaylistIndex;
            _overlay.ControlsBar.SetQueueControls(count > 1, idx > 0, idx < count - 1, player_queue_split_view.ShowSidebar);
            return false;
        });
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _player.UpdatePreferences(preferences);
        _overlay.Shortcuts.UpdateBindings(preferences.Shortcuts);
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        _session.EndSession();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCommentsSidebarNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "show-sidebar" && Widget.ShowSidebar)
        {
            _overlay.CommentsCue.Hide();
            _commentsView.EnsureLoaded();
        }
    }

    private void OnQueueSidebarNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "show-sidebar")
        {
            var count = _queueService.Items.Count;
            var idx = _session.CurrentPlaylistIndex;
            _overlay.ControlsBar.SetQueueControls(count > 1, idx > 0, idx < count - 1, player_queue_split_view.ShowSidebar);
        }
    }

    private sealed class EmbeddedPlayerPlaybackService(EmbeddedPlayerView player) : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request) => player.PresentAsync(request);
    }
}
