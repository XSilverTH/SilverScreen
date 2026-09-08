using System.Collections.Immutable;
using Adw;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player.Comments;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.RichText;
using SilverScreen.Queue;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
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

public partial class EmbeddedPlayerView : ViewBase<OverlaySplitView>, IEmbeddedPlayerPresenter
{
    private const double MinimumPlaybackSpeed = 0.25;
    private const double MaximumPlaybackSpeed = 4;
    private const double PlaybackSpeedIncrement = 0.25;

    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    private readonly Action _backRequested;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly PlayerLinkRouter _linkRouter;
    private readonly Action<string> _searchRequested;
    private readonly PlayerChapterOverlay _chapterOverlay;
    private readonly PlayerChromeController _chromeController;
    private readonly CommentsView _commentsView;
    private readonly DesktopMediaIntegration _desktopMedia;
    private readonly PlayerEngagementController _engagement;
    private readonly VideoInfoPanelController _infoPanel;
    private readonly PlayerOsdController _osdController;
    private readonly LibMpvPlayer _player;
    private readonly IPreferencesService _preferences;
    private readonly Action _presentRequested;
    private readonly IQueueService _queueService;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly PlayerResumeController _resumeController;
    private readonly Action<VideoSummary> _videoRequested;
    private readonly PlaybackSession _session;
    private readonly PlayerShortcutController _shortcutController;
    private readonly PlayerSponsorBlockController _sponsorBlockController;
    private readonly PlayerStatsController _statsController;
    private readonly PlayerSubtitleController _subtitleController;
    private readonly PlayerTimelineController _timelineController;

    private string? _commentsVideoId;
    private bool _isMuted;
    private PlaybackRequest? _loadedRequest;
    private bool _rendererReady;
    private double _speed = 1;
    private bool _syncingQueue;
    private bool _updatingControls;
    private double _volume = 100;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, Action<VideoSummary> channelRequested,
        Action<VideoSummary> videoRequested, Action<string> searchRequested,
        PlayerDependencies dependencies)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _channelRequested = channelRequested;
        _videoRequested = videoRequested ?? throw new ArgumentNullException(nameof(videoRequested));
        _searchRequested = searchRequested ?? throw new ArgumentNullException(nameof(searchRequested));
        _linkRouter = new PlayerLinkRouter(
            SeekToTimestamp,
            video => _videoRequested(video),
            (channelUrl, displayName) => OpenLinkedChannel(channelUrl, displayName),
            tag => _searchRequested("#" + tag),
            OpenExternalLink);
        _preferences = dependencies.Preferences;
        _commentsView = Lifetime.Own(new CommentsView(new CommentsViewModel(dependencies.Comments), CloseComments,
            OnRichLinkActivated));
        comments_sidebar_host.Append(_commentsView.Widget);
        _queueService = dependencies.Queue;
        _queueViewModel = Lifetime.Own(new QueueViewModel(dependencies.Queue, new EmbeddedPlayerPlaybackService(this)));
        _queueView = Lifetime.Own(new QueueView(_queueViewModel, dependencies.Thumbnails, CloseQueue,
            OnTrackJumpRequested));
        player_queue_sidebar_host.Append(_queueView.Widget);
        player_queue_button.BindProperty("active", player_queue_split_view, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        Lifetime.Track(() => _queueService.Changed += OnQueueChanged, () => _queueService.Changed -= OnQueueChanged);
        _player = Lifetime.Own(new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!IsDisposed) action();
            return false;
        })));
        _timelineController = Lifetime.Own(new PlayerTimelineController(player_timeline, player_timeline_overlay, player_scrub_cue,
            player_scrub_time_label, player_scrub_delta_label, player_scrub_chapter_label, player_position_label,
            player_duration_label,
            (pos, exact) => _player.SeekAbsolute(pos, exact), RegisterActivity));
        _desktopMedia = Lifetime.Own(new DesktopMediaIntegration(_player, presentRequested));
        _session = Lifetime.Own(new PlaybackSession(dependencies, _desktopMedia));
        Lifetime.Track(() => _session.SeekRequested += SeekAbsolute, () => _session.SeekRequested -= SeekAbsolute);
        Lifetime.Track(() => _session.VideoChanged += OnSessionVideoChanged, () => _session.VideoChanged -= OnSessionVideoChanged);
        Lifetime.Track(() => _session.SessionEnded += OnSessionEnded, () => _session.SessionEnded -= OnSessionEnded);
        Lifetime.Track(() => _session.Failed += OnSessionFailed, () => _session.Failed -= OnSessionFailed);
        _engagement = Lifetime.Own(new PlayerEngagementController(_session, player_like_button, player_like_image,
            player_likes_label, player_dislike_button, player_dislike_image, player_dislikes_label));
        _chapterOverlay = Lifetime.Own(new PlayerChapterOverlay(player_timeline_overlay, player_timeline,
            () => _timelineController.PlaybackPosition, pos => SeekAbsolute(pos), RegisterActivity));
        _sponsorBlockController = Lifetime.Own(new PlayerSponsorBlockController(_session, _preferences,
            player_timeline,
            player_timeline_overlay, player_sponsorblock_revealer, player_sponsorblock_skip_button,
            player_sponsorblock_label));
        _resumeController = Lifetime.Own(new PlayerResumeController(_session,
            player_resume_revealer, player_resume_button, player_resume_label,
            player_restart_revealer, player_restart_button, player_restart_label));
        _infoPanel = Lifetime.Own(new VideoInfoPanelController(dependencies.MediaResolver, _channelRequested, player_info_backdrop,
            player_info_cue_revealer, player_info_revealer, player_info_title_label, player_info_channel_label,
            player_info_stats_label, player_info_status_label, player_info_description_scroller,
            player_info_description, player_info_close_button, OnRichLinkActivated, () =>
            {
                if (_session.HasMedia) player_surface.GrabFocus();
            }));
        _subtitleController = Lifetime.Own(new PlayerSubtitleController(_preferences, player_subtitle_dropdown,
            player_subtitle_model,
            player_subtitle_button, trackId => _player.SelectSubtitleTrack(trackId)));
        Lifetime.Track(() => _player.RenderRequested += OnRenderRequested, () => _player.RenderRequested -= OnRenderRequested);
        Lifetime.Track(() => _player.StateChanged += OnStateChanged, () => _player.StateChanged -= OnStateChanged);
        Lifetime.Track(() => _player.PlaybackFailed += OnPlaybackFailed, () => _player.PlaybackFailed -= OnPlaybackFailed);
        SetControls(100, 1, "Best");
        _osdController = Lifetime.Own(new PlayerOsdController(_preferences, player_osd_revealer, player_osd_icon, player_osd_label));
        _statsController = Lifetime.Own(new PlayerStatsController(new PlayerStatsProvider(_player), player_stats_revealer, player_stats_label));
        _chromeController = Lifetime.Own(new PlayerChromeController(
            Widget,
            player_header_bar,
            player_center_controls,
            player_controls,
            () => player_volume_popover.GetVisible() || player_settings_popover.GetVisible() || _infoPanel.IsOpen,
            () => _chapterOverlay.Layout(),
            UpdatePointer,
            visible => _osdController.SetChromeVisible(visible)));
        _osdController.SetChromeVisible(true);
        _shortcutController = Lifetime.Own(new PlayerShortcutController(Widget));
        _shortcutController.KeyInterceptor = keyval => _statsController.HandleKeyPress(keyval);
        _shortcutController.RegisterAction(PlayerShortcutActions.TogglePause, () =>
        {
            IsPaused = !IsPaused;
            _player.TogglePause();
            _osdController.ShowPlayPause(IsPaused);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekBackward, () =>
        {
            SeekRelative(-10);
            _osdController.ShowSeek(-10);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekForward, () =>
        {
            SeekRelative(10);
            _osdController.ShowSeek(10);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.StepFrameBackward, () => _player.StepFrame(false));
        _shortcutController.RegisterAction(PlayerShortcutActions.StepFrameForward, () => _player.StepFrame(true));
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleMute, () =>
        {
            _isMuted = !_isMuted;
            _player.ToggleMute();
            _osdController.ShowVolume(_volume, _isMuted);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.VolumeUp, () =>
        {
            _volume = Math.Clamp(_volume + 5, 0, 100);
            _isMuted = false;
            _player.AdjustVolume(5);
            _osdController.ShowVolume(_volume, _isMuted);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.VolumeDown, () =>
        {
            _volume = Math.Clamp(_volume - 5, 0, 100);
            _isMuted = false;
            _player.AdjustVolume(-5);
            _osdController.ShowVolume(_volume, _isMuted);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekToBeginning, () =>
        {
            SeekAbsolute(0);
            _osdController.ShowSeekToBeginning();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ReturnToShell, () =>
        {
            if (_statsController.IsOpen) _statsController.Close();
            else if (_timelineController.IsScrubbing) _timelineController.CancelScrubbing();
            else if (player_queue_button.Active) player_queue_button.Active = false;
            else if (Widget.ShowSidebar) CloseComments();
            else if (_infoPanel.IsOpen) _infoPanel.Close();
            else ReturnToShell();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleQueue, () =>
        {
            player_queue_button.Active = !player_queue_button.Active;
            _osdController.ShowQueue(player_queue_button.Active);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleVideoInfo, () =>
        {
            _infoPanel.Toggle(_session.CurrentVideo);
            _osdController.ShowVideoInfo(_infoPanel.IsOpen);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleStats, () => { _statsController.Toggle(); });
        _shortcutController.RegisterAction(PlayerShortcutActions.SpeedDecrease, () =>
        {
            var speed = AdjustSpeed(-1);
            _osdController.ShowSpeed(speed);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.SpeedIncrease, () =>
        {
            var speed = AdjustSpeed(1);
            _osdController.ShowSpeed(speed);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.NextVideo, () =>
        {
            _player.MovePlaylist(true);
            _osdController.ShowNextVideo();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.PreviousVideo, () =>
        {
            _player.MovePlaylist(false);
            _osdController.ShowPreviousVideo();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleFullscreen, () =>
        {
            var isFullscreen = ToggleFullscreen();
            _osdController.ShowFullscreen(isFullscreen);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.PreferredSubtitle, () =>
        {
            var state = ShowPreferredSubtitle();
            _osdController.ShowSubtitles(state);
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ResumeOrSkip, () =>
        {
            if (_session.TryResume())
                _osdController.ShowResumed();
            else if (_session.TrySkipManualSegment())
                _osdController.ShowSkippedSponsor();
        });
        _shortcutController.UpdateBindings(_preferences.GetPreferences().Shortcuts);
        Lifetime.Track(
            () => _preferences.PreferencesChanged += OnPreferencesChanged,
            () => _preferences.PreferencesChanged -= OnPreferencesChanged);
        Lifetime.Track(
            () => Widget.OnNotify += OnWidgetNotify,
            () => Widget.OnNotify -= OnWidgetNotify);
    }

    private void OnWidgetNotify(GObject.Object sender, GObject.Object.NotifySignalArgs e)
    {
        if (e.Pspec.GetName() != "visible") return;
        if (Widget.GetVisible())
            _shortcutController.Attach();
        else
            _shortcutController.Detach();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_rendererReady)
            {
                player_surface.MakeCurrent();
                _player.ShutdownRenderer();
                _rendererReady = false;
            }
        }

        base.Dispose(disposing);
    }

    public bool HasMedia => _session.HasMedia;
    public bool IsPaused { get; private set; } = true;

    public event EventHandler? PlaybackStateChanged;

    public Task TogglePauseAsync()
    {
        _player.TogglePause();
        return Task.CompletedTask;
    }

    public Task<string> PresentAsync(PlaybackRequest request)
    {
        try
        {
            _ = MpvCommandBuilder.GetPlaybackUrls(request);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to resolve playback URLs for request");
            return Task.FromResult(exception.Message);
        }

        if (!_player.IsAvailable)
            return Task.FromResult(_player.AvailabilityError ?? RuntimeDependencyGuidance.LibMpvUnavailable);
        Functions.IdleAdd(0, () =>
        {
            EndSession(true);
            _session.Start(request);
            _syncingQueue = true;
            try
            {
                _queueService.Replace(request.Videos);
            }
            finally
            {
                _syncingQueue = false;
            }

            _queueViewModel.SetCurrentPlayingIndex(0);
            var preferences = _preferences.GetPreferences();
            var firstVideo = request.Videos[0];
            _timelineController.Reset();
            _timelineController.SetDuration(firstVideo.Duration);
            _infoPanel.SetVideo(firstVideo);
            RegisterActivity();
            _chapterOverlay.Update([], TimeSpan.Zero);

            SetControls(100, 1, NormalizeQuality(preferences.Quality.ToPersistedString()));
            SetLoading(true);
            player_queue_controls.SetVisible(request.Videos.Length > 1);
            player_previous_queue_button.Sensitive = false;
            player_next_queue_button.Sensitive = request.Videos.Length > 1;
            _presentRequested();
            _shortcutController.Attach();
            Widget.GrabFocus();
            if (!_rendererReady) return false;
            _player.Load(request, preferences, _session.CookieFilePath);
            _loadedRequest = request;

            return false;
        });

        return Task.FromResult("Opening embedded player.");
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _shortcutController.UpdateBindings(preferences.Shortcuts);
    }

    /// <summary>
    ///     Re-realizes the OpenGL surface when navigating back to the player view,
    ///     allowing the player engine to resume playback seamlessly at the captured reload position and state.
    /// </summary>
    private void OnPlayerSurfaceRealize(object? sender, EventArgs args)
    {
        player_surface.MakeCurrent();
        if (player_surface.GetError() is not null)
        {
            OnPlaybackFailed(this, "Unable to create an OpenGL context for embedded playback.");
            return;
        }

        _player.InitializeRenderer();
        _rendererReady = true;
        _shortcutController.Attach();

        if (_session.Request is null || ReferenceEquals(_loadedRequest, _session.Request)) return;
        _player.Load(_session.Request, _preferences.GetPreferences(), _session.CookieFilePath);
        _loadedRequest = _session.Request;
    }

    /// <summary>
    ///     Unrealizes the OpenGL surface when navigating away from the player view,
    ///     capturing active playback position, playlist index, and pause state for restoration.
    /// </summary>
    private void OnPlayerSurfaceUnrealize(object? sender, EventArgs args)
    {
        player_surface.MakeCurrent();
        _player.ShutdownRenderer();
        _rendererReady = false;
    }

    private bool OnPlayerSurfaceRender(GLArea sender, GLArea.RenderSignalArgs args)
    {
        if (IsDisposed || !_rendererReady) return false;
        _player.Render(player_surface.GetAllocatedWidth() * player_surface.GetScaleFactor(),
            player_surface.GetAllocatedHeight() * player_surface.GetScaleFactor());
        return true;
    }


    private void SeekAbsolute(double position, bool exact = true)
    {
        _timelineController.SeekAbsolute(position, exact);
    }


    private void SeekRelative(double offset)
    {
        _player.SeekRelative(offset);
    }

    private double AdjustSpeed(int direction)
    {
        var newSpeed = Math.Clamp(SnapPlaybackSpeed(_speed) + direction * PlaybackSpeedIncrement,
            MinimumPlaybackSpeed, MaximumPlaybackSpeed);
        _speed = newSpeed;
        _player.SetSpeed(newSpeed);
        return newSpeed;
    }

    private bool ToggleFullscreen()
    {
        if (Widget.GetRoot() is not Window window) return false;
        window.Fullscreened = !window.Fullscreened;
        return window.Fullscreened;
    }

    private void RegisterActivity()
    {
        _chromeController.RegisterActivity();
    }

    private void OnBackButtonClicked(object? sender, EventArgs args)
    {
        ReturnToShell();
    }

    private void OnFullscreenButtonClicked(object? sender, EventArgs args)
    {
        ToggleFullscreen();
    }

    private void ReturnToShell()
    {
        _shortcutController.Detach();
        EndSession(true);
        _backRequested();
    }

    private void OnPreviousQueueButtonClicked(object? sender, EventArgs args)
    {
        _player.MovePlaylist(false);
    }

    private void OnNextQueueButtonClicked(object? sender, EventArgs args)
    {
        _player.MovePlaylist(true);
    }


    private void CloseQueue()
    {
        player_queue_button.Active = false;
    }

    private void OnTrackJumpRequested(int index)
    {
        if (_session.Request is null || index < 0 || index >= _session.Request.Videos.Length)
            return;

        _player.PlayPlaylistIndex(index);
    }

    private void OnRewindButtonClicked(object? sender, EventArgs args)
    {
        SeekRelative(-10);
    }

    private void OnForwardButtonClicked(object? sender, EventArgs args)
    {
        SeekRelative(10);
    }

    private void OnPlayPauseButtonClicked(object? sender, EventArgs args)
    {
        _player.TogglePause();
    }

    private void OnLikeButtonClicked(object? sender, EventArgs args)
    {
        _session.SubmitVote(VideoVote.Like);
    }

    private void OnDislikeButtonClicked(object? sender, EventArgs args)
    {
        _session.SubmitVote(VideoVote.Dislike);
    }

    private void OnPlayerChannelButtonClicked(object? sender, EventArgs args)
    {
        OpenCurrentChannel();
    }


    private void OpenCurrentChannel()
    {
        if (_session.CurrentVideo is not { } video) return;
        _shortcutController.Detach();
        _channelRequested(video);
    }

    private void OnSponsorBlockSkipButtonClicked(object? sender, EventArgs args)
    {
        if (_session.TrySkipManualSegment())
            RegisterActivity();
    }

    private void OnResumeButtonClicked(object? sender, EventArgs args)
    {
        if (_resumeController.TryConsumeSeekPrompt(out var returnPosition))
        {
            SeekBackTo(returnPosition);
            RegisterActivity();
        }
        else if (_session.TryResume())
        {
            RegisterActivity();
        }
    }

    private bool OnRichLinkActivated(string uri)
    {
        return _linkRouter.Activate(uri);
    }

    private void SeekToTimestamp(double positionSeconds)
    {
        if (!_session.HasMedia)
            return;

        var previousPosition = _timelineController.PlaybackPosition;
        SeekAbsolute(positionSeconds);
        _osdController.ShowSeek(SeekDeltaSeconds(previousPosition, positionSeconds));
        _resumeController.ShowSeekPrompt(previousPosition);
        RegisterActivity();
    }

    private void SeekBackTo(TimeSpan returnPosition)
    {
        var currentPosition = _timelineController.PlaybackPosition;
        SeekAbsolute(returnPosition.TotalSeconds);
        _osdController.ShowSeek(SeekDeltaSeconds(currentPosition, returnPosition.TotalSeconds));
    }

    private static int SeekDeltaSeconds(TimeSpan from, double toSeconds)
    {
        return (int)Math.Round(toSeconds - from.TotalSeconds, MidpointRounding.AwayFromZero);
    }

    private void OpenLinkedChannel(string channelUrl, string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? channelUrl : displayName;
        _channelRequested(new VideoSummary(
            string.Empty,
            name,
            name,
            TimeSpan.Zero,
            string.Empty,
            false,
            null,
            null,
            null,
            channelUrl));
    }

    private void OpenExternalLink(string url)
    {
        try
        {
            if (Widget.GetRoot() is not Window parent)
            {
                Logger.Warning("Cannot open link {Url} without a parent window", url);
                return;
            }

            UriLauncher.New(url).LaunchAsync(parent).ContinueWith(task =>
            {
                if (task.IsFaulted)
                    Logger.Warning(task.Exception, "Failed to open link {Url} in browser", url);
            }, TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to open link {Url} in browser", url);
        }
    }

    private void OnRestartButtonClicked(object? sender, EventArgs args)
    {
        if (_session.TryRestart())
            RegisterActivity();
    }


    private void OnInfoBackdropClicked(object? sender, EventArgs args)
    {
        _infoPanel.Close();
    }

    private void OnInfoCueButtonClicked(object? sender, EventArgs args)
    {
        _infoPanel.Show();
    }

    private void OnInfoCloseButtonClicked(object? sender, EventArgs args)
    {
        _infoPanel.Close();
    }

    private void OnInfoChannelButtonClicked(object? sender, EventArgs args)
    {
        _infoPanel.OpenChannel();
    }

    private void OnSubtitleButtonClicked(object? sender, EventArgs args)
    {
        ShowPreferredSubtitle();
    }

    private string ShowPreferredSubtitle()
    {
        return _subtitleController.ShowPreferredSubtitle();
    }

    private void UpdatePointer(double x, double y)
    {
        var height = Widget.GetAllocatedHeight();
        var width = Widget.GetAllocatedWidth();
        _infoPanel.UpdatePointer(x, y, width, height, _session.HasMedia);
        UpdateCommentsCue(x, y, width, height);
    }

    private void UpdateCommentsCue(double x, double y, double width, double height)
    {
        if (!_session.HasMedia || Widget.ShowSidebar || _infoPanel.IsOpen || width <= 0 || height <= 0)
        {
            if (player_comments_cue_revealer.RevealChild)
                player_comments_cue_revealer.RevealChild = false;
            return;
        }

        var isVisible = player_comments_cue_revealer.RevealChild;
        var inZone = PlayerCueGeometry.IsCommentsCueActive(x, y, width, height, isVisible);
        if (isVisible != inZone)
            player_comments_cue_revealer.RevealChild = inZone;
    }

    private void OnCommentsCueButtonClicked(object? sender, EventArgs args)
    {
        OpenComments();
    }

    private void OpenComments()
    {
        if (!_session.HasMedia) return;
        Widget.ShowSidebar = true;
        player_comments_cue_revealer.RevealChild = false;
        _commentsView.EnsureLoaded();
    }

    private void CloseComments()
    {
        Widget.ShowSidebar = false;
    }

    private void OnVolumeScaleValueChanged(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetVolume(player_volume_scale.GetValue());
    }

    private void OnQualityDropdownNotify(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetQuality(QualityAt(player_quality_dropdown.GetSelected()));
    }

    private void OnSpeedScaleValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls) return;

        var speed = SnapPlaybackSpeed(player_speed_scale.GetValue());
        if (Math.Abs(player_speed_scale.GetValue() - speed) > 0.0001)
        {
            _updatingControls = true;
            try
            {
                player_speed_scale.SetValue(speed);
            }
            finally
            {
                _updatingControls = false;
            }
        }

        SetSpeedLabel(speed);
        _player.SetSpeed(speed);
    }

    private void OnSubtitleDropdownNotify(object? sender, EventArgs args)
    {
        if (_updatingControls) return;
        _subtitleController.OnSelectionChanged();
    }


    private void OnRenderRequested(object? sender, EventArgs args)
    {
        if (IsDisposed) return;

        player_surface.QueueRender();
    }

    private void OnStateChanged(object? sender, LibMpvPlaybackState state)
    {
        if (IsDisposed) return;
        _speed = state.Speed;
        IsPaused = state.IsPaused;
        _volume = state.Volume;
        _isMuted = state.IsMuted;
        _session.UpdatePlayback(state);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);

        SetLoading(state.IsLoading);
        _updatingControls = true;
        _subtitleController.UpdateTracks(state.SubtitleTracks, _updatingControls);
        try
        {
            _timelineController.UpdatePosition(state);
            _chapterOverlay.Update(state.Chapters, state.Duration);

            player_play_pause_button.SetIconName(state is { HasMedia: true, IsPaused: false }
                ? "media-playback-pause-symbolic"
                : "media-playback-start-symbolic");
            player_play_pause_button.SetTooltipText(state is { HasMedia: true, IsPaused: false }
                ? "Pause (Space or K)"
                : "Play (Space or K)");
            player_volume_scale.SetValue(Math.Clamp(state.Volume, 0, 100));
            player_volume_button.SetIconName(VolumeIcon(state.Volume, state.IsMuted));
            var speed = SnapPlaybackSpeed(state.Speed);
            player_speed_scale.SetValue(speed);
            SetSpeedLabel(speed);
            if (_session.Request is not { } request || state.PlaylistIndex is < 0 or >= int.MaxValue ||
                state.PlaylistIndex >= request.Videos.Length) return;
            _queueViewModel.SetCurrentPlayingIndex(state.PlaylistIndex);
            player_previous_queue_button.Sensitive = state.PlaylistIndex > 0;
            player_next_queue_button.Sensitive = state.PlaylistIndex < request.Videos.Length - 1;
            _sponsorBlockController.Redraw();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void OnSessionVideoChanged(VideoSummary video, int playlistIndex)
    {
        _infoPanel.SetVideo(video);
        player_title_label.SetText(video.Title);
        player_channel_label.SetText(video.ChannelName);
        if (string.Equals(_commentsVideoId, video.Id, StringComparison.Ordinal)) return;

        _commentsVideoId = video.Id;
        _commentsView.SetVideo(video.Id);
        if (Widget.ShowSidebar)
            _commentsView.EnsureLoaded();
    }

    private void OnPlaybackFailed(object? sender, string detail)
    {
        _session.Fail(detail);
    }

    private void OnSessionFailed(string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _infoPanel.Close();
        _infoPanel.SetVideo(null);
        player_title_label.SetText("Playback failed");
        SetLoading(false);
        player_channel_label.SetText($"Embedded playback failed: {detail}");
        _timelineController.Reset();
        _osdController.HideImmediate();
        player_play_pause_button.SetIconName("media-playback-start-symbolic");


        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        CloseComments();
        player_comments_cue_revealer.RevealChild = false;
        player_queue_controls.SetVisible(false);
        player_queue_button.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _player.Stop();
        _loadedRequest = null;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        _session.EndSession();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionEnded()
    {
        _timelineController.Reset();
        _osdController.HideImmediate();
        _loadedRequest = null;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        _infoPanel.SetVideo(null);
        player_queue_controls.SetVisible(false);
        player_queue_button.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);


        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        CloseComments();
        player_comments_cue_revealer.RevealChild = false;
        SetLoading(false);
    }

    private void SetLoading(bool loading)
    {
        player_loading_indicator.SetVisible(loading);
        player_center_controls.SetVisible(!loading);
    }

    private void SetControls(double volume, double speed, string quality)
    {
        _updatingControls = true;
        _volume = volume;
        _speed = speed;
        _isMuted = false;
        try
        {
            player_volume_scale.SetValue(volume);
            player_volume_button.SetIconName(VolumeIcon(volume, false));
            var normalizedSpeed = SnapPlaybackSpeed(speed);
            player_speed_scale.SetValue(normalizedSpeed);
            SetSpeedLabel(normalizedSpeed);
            player_quality_dropdown.SetSelected(
                (uint)Array.IndexOf(["Best", "1080p", "720p", "480p", "360p"], quality));
        }
        finally
        {
            _updatingControls = false;
        }
    }


    private static string NormalizeQuality(string quality)
    {
        return MpvCommandBuilder.BuildYtdlFormat(quality) is null && quality != "Best" ? "Best" : quality;
    }

    private static string QualityAt(uint selected)
    {
        return new[] { "Best", "1080p", "720p", "480p", "360p" }[(int)Math.Min(selected, 4)];
    }

    private static double SnapPlaybackSpeed(double speed)
    {
        var clampedSpeed = Math.Clamp(speed, MinimumPlaybackSpeed, MaximumPlaybackSpeed);
        var steps = Math.Round((clampedSpeed - MinimumPlaybackSpeed) / PlaybackSpeedIncrement,
            MidpointRounding.AwayFromZero);
        return MinimumPlaybackSpeed + steps * PlaybackSpeedIncrement;
    }

    private void SetSpeedLabel(double speed)
    {
        player_speed_label.SetText($"{speed:0.##}×");
    }

    private static string VolumeIcon(double volume, bool muted)
    {
        return muted || volume <= 0 ? "audio-volume-muted-symbolic" :
            volume <= 50 ? "audio-volume-low-symbolic" : "audio-volume-high-symbolic";
    }

    private void OnQueueChanged(object? sender, EventArgs args)
    {
        Functions.IdleAdd(0, () =>
        {
            if (IsDisposed || _syncingQueue || _session.Request is null)
                return false;

            var currentVideos = _session.Request.Videos;
            var newItems = _queueService.Items;
            var newVideos = newItems.Select(i => i.Video).ToImmutableArray();

            if (newVideos.SequenceEqual(currentVideos))
                return false;

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
                var fromIndex = -1;
                var toIndex = -1;
                for (var i = 0; i < currentVideos.Length; i++)
                    if (currentVideos[i].Id != newVideos[i].Id)
                    {
                        fromIndex = i;
                        toIndex = newVideos.IndexOf(currentVideos[i]);
                        break;
                    }

                if (fromIndex >= 0 && toIndex >= 0) _player.MovePlaylistItem(fromIndex, toIndex);
            }

            _session.UpdateQueue(newVideos);
            player_queue_controls.SetVisible(newVideos.Length > 1);
            player_previous_queue_button.Sensitive = _session.CurrentPlaylistIndex > 0;
            player_next_queue_button.Sensitive = _session.CurrentPlaylistIndex < newVideos.Length - 1;
            return false;
        });
    }


    private sealed class EmbeddedPlayerPlaybackService(EmbeddedPlayerView player) : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request)
        {
            return player.PresentAsync(request);
        }
    }

    private sealed class PlayerStatsProvider(LibMpvPlayer player) : IPlayerStatsProvider
    {
        public PlaybackStats? GetPlaybackStats()
        {
            return player.GetPlaybackStats();
        }
    }
}