using System.Collections.Immutable;
using System.Globalization;
using Adw;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.ViewModels;
using SilverScreen.Views.Comments;
using SilverScreen.Views.Queue;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Window = Gtk.Window;

namespace SilverScreen.Views.Player;

internal interface IEmbeddedPlayerPresenter
{
    Task<string> PresentAsync(PlaybackRequest request);
}

public partial class EmbeddedPlayerView : ViewBase<OverlaySplitView>, IEmbeddedPlayerPresenter, IDisposable
{
    private const double MinimumPlaybackSpeed = 0.25;
    private const double MaximumPlaybackSpeed = 5;
    private const double PlaybackSpeedIncrement = 0.25;

    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    [BlueprintWidget("player_surface")]
    private GLArea _playerSurface = null!;

    [BlueprintWidget("player_header_bar")]
    private Widget _headerBar = null!;

    [BlueprintWidget("player_center_controls")]
    private Box _centerControls = null!;

    [BlueprintWidget("player_controls")]
    private Widget _playerControls = null!;

    [BlueprintWidget("player_play_pause_button")]
    private Button _playPauseButton = null!;

    [BlueprintWidget("player_volume_button")]
    private MenuButton _volumeButton = null!;

    [BlueprintWidget("player_volume_scale")]
    private Scale _volumeScale = null!;

    [BlueprintWidget("player_volume_popover")]
    private Popover _volumePopover = null!;

    [BlueprintWidget("player_settings_popover")]
    private Popover _settingsPopover = null!;

    [BlueprintWidget("player_quality_dropdown")]
    private DropDown _qualityDropdown = null!;

    [BlueprintWidget("player_queue_controls")]
    private Box _queueControls = null!;

    [BlueprintWidget("player_speed_label")]
    private Label _speedLabel = null!;

    [BlueprintWidget("player_speed_scale")]
    private Scale _speedScale = null!;

    [BlueprintWidget("player_subtitle_dropdown")]
    private DropDown _subtitleDropdown = null!;

    [BlueprintWidget("player_subtitle_model")]
    private StringList _subtitleModel = null!;

    [BlueprintWidget("player_timeline")]
    private Scale _timeline = null!;

    [BlueprintWidget("player_timeline_overlay")]
    private Overlay _timelineOverlay = null!;

    [BlueprintWidget("player_scrub_cue")]
    private Box _scrubCue = null!;

    [BlueprintWidget("player_scrub_time_label")]
    private Label _scrubTimeLabel = null!;

    [BlueprintWidget("player_scrub_delta_label")]
    private Label _scrubDeltaLabel = null!;

    [BlueprintWidget("player_scrub_chapter_label")]
    private Label _scrubChapterLabel = null!;

    [BlueprintWidget("player_sponsorblock_skip_button")]
    private Button _sponsorBlockSkipButton = null!;

    [BlueprintWidget("player_resume_button")]
    private Button _resumeButton = null!;

    [BlueprintWidget("player_restart_button")]
    private Button _restartButton = null!;

    [BlueprintWidget("player_position_label")]
    private Label _positionLabel = null!;

    [BlueprintWidget("player_duration_label")]
    private Label _durationLabel = null!;

    [BlueprintWidget("player_loading_indicator")]
    private Box _loadingIndicator = null!;

    [BlueprintWidget("player_title_label")]
    private Label _titleLabel = null!;

    [BlueprintWidget("player_channel_label")]
    private Label _channelLabel = null!;

    [BlueprintWidget("player_likes_label")]
    private Label _likesLabel = null!;

    [BlueprintWidget("player_like_button")]
    private Button _likeButton = null!;

    [BlueprintWidget("player_like_image")]
    private Image _likeImage = null!;

    [BlueprintWidget("player_dislike_button")]
    private Button _dislikeButton = null!;

    [BlueprintWidget("player_dislikes_label")]
    private Label _dislikesLabel = null!;

    [BlueprintWidget("player_dislike_image")]
    private Image _dislikeImage = null!;

    [BlueprintWidget("player_subtitle_button")]
    private Button _subtitleButton = null!;

    [BlueprintWidget("player_comments_button")]
    private ToggleButton _commentsButton = null!;

    [BlueprintWidget("comments_sidebar_host")]
    private Box _commentsSidebarHost = null!;

    [BlueprintWidget("player_queue_split_view")]
    private OverlaySplitView _queueSplitView = null!;

    [BlueprintWidget("player_queue_button")]
    private ToggleButton _queueButton = null!;

    [BlueprintWidget("player_previous_queue_button")]
    private Button _previousQueueButton = null!;

    [BlueprintWidget("player_next_queue_button")]
    private Button _nextQueueButton = null!;

    [BlueprintWidget("player_queue_sidebar_host")]
    private Box _playerQueueSidebarHost = null!;

    [BlueprintWidget("player_info_host")]
    private Box _playerInfoHost = null!;

    private readonly Action _backRequested;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly PlayerChapterOverlay _chapterOverlay;
    private readonly CommentsView _commentsView;
    private readonly DesktopMediaIntegration _desktopMedia;
    private readonly PlayerEngagementController _engagement;
    private readonly ImmutableArray<IPlayerFeature> _features;
    private readonly VideoInfoPanelView _infoPanel;
    private readonly PlaybackSession _session;
    private readonly LibMpvPlayer _player;
    private readonly IPreferencesService _preferences;
    private readonly Action _presentRequested;
    private readonly IQueueService _queueService;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly PlayerResumeController _resumeController;
    private readonly PlayerSponsorBlockController _sponsorBlockController;
    private readonly PlayerSubtitleController _subtitleController;
    private readonly PlayerTimelineController _timelineController;
    private readonly PlayerChromeController _chromeController;
    private readonly PlayerShortcutController _shortcutController;
    private string? _commentsVideoId;
    private bool _disposed;

    private bool _rendererReady;
    private double _speed = 1;
    private bool _syncingQueue;
    private bool _updatingControls;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, Action<VideoSummary> channelRequested,
        PlayerDependencies dependencies)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _channelRequested = channelRequested;
        _preferences = dependencies.Preferences;
        _commentsView = new CommentsView(new CommentsViewModel(dependencies.Comments), CloseComments);
        _commentsSidebarHost.Append(_commentsView.Widget);
        _commentsButton.BindProperty("active", Widget, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);

        _queueService = dependencies.Queue;
        _queueViewModel = new QueueViewModel(dependencies.Queue, new EmbeddedPlayerPlaybackService(this));
        _queueView = new QueueView(_queueViewModel, dependencies.Thumbnails, dependencies.WatchProgress, CloseQueue,
            OnTrackJumpRequested);
        _playerQueueSidebarHost.Append(_queueView.Widget);
        _queueButton.BindProperty("active", _queueSplitView, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        _queueService.Changed += OnQueueChanged;
        _player = new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!_disposed) action();
            return false;
        }));
        _timelineController = new PlayerTimelineController(_timeline, _timelineOverlay, _scrubCue,
            _scrubTimeLabel, _scrubDeltaLabel, _scrubChapterLabel, _positionLabel, _durationLabel,
            (pos, exact) => _player.SeekAbsolute(pos, exact), RegisterActivity);
        _engagement = new PlayerEngagementController(dependencies.VideoEngagement, dependencies.YouTubeRating,
            dependencies.Session, _likeButton, _likeImage, _likesLabel, _dislikeButton, _dislikeImage, _dislikesLabel);
        _chapterOverlay = new PlayerChapterOverlay(_timelineOverlay, _timeline,
            () => _timelineController.PlaybackPosition, pos => SeekAbsolute(pos), RegisterActivity);
        _sponsorBlockController = new PlayerSponsorBlockController(dependencies.SponsorBlock, _preferences, _timeline,
            _timelineOverlay, _sponsorBlockSkipButton, pos => SeekAbsolute(pos));
        _resumeController = new PlayerResumeController(_preferences, dependencies.WatchProgress, _resumeButton, _restartButton,
            pos => SeekAbsolute(pos));
        _features = [_engagement, _sponsorBlockController, _resumeController];
        _desktopMedia = new DesktopMediaIntegration(_player, _presentRequested);
        _session = new PlaybackSession(dependencies.CookieFiles, dependencies.PlaybackPresence,
            dependencies.PlaybackTelemetry, dependencies.WatchProgress, _desktopMedia);
        _session.VideoChanged += OnSessionVideoChanged;
        _session.SessionEnded += OnSessionEnded;
        _session.Failed += OnSessionFailed;
        _infoPanel = new VideoInfoPanelView(dependencies.VideoDetails, _channelRequested, () =>
        {
            if (_session.HasMedia) _playerSurface.GrabFocus();
        });
        _playerInfoHost.Append(_infoPanel.Widget);
        _subtitleController = new PlayerSubtitleController(_preferences, _subtitleDropdown, _subtitleModel,
            _subtitleButton, trackId => _player.SelectSubtitleTrack(trackId));
        _player.RenderRequested += OnRenderRequested;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        SetControls(100, 1, "Best");
        _chromeController = new PlayerChromeController(
            Widget,
            _headerBar,
            _centerControls,
            _playerControls,
            () => _volumePopover.GetVisible() || _settingsPopover.GetVisible() || _infoPanel.IsOpen,
            () => _chapterOverlay.Layout(),
            y => _infoPanel.UpdatePointer(y, Widget.GetAllocatedHeight(), _session.HasMedia));
        _shortcutController = new PlayerShortcutController(Widget, () => _session.HasMedia, RegisterActivity);
        _shortcutController.RegisterAction(PlayerShortcutActions.TogglePause, () => _player.TogglePause());
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekBackward, () => SeekRelative(-10));
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekForward, () => SeekRelative(10));
        _shortcutController.RegisterAction(PlayerShortcutActions.StepFrameBackward, () => _player.StepFrame(false));
        _shortcutController.RegisterAction(PlayerShortcutActions.StepFrameForward, () => _player.StepFrame(true));
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleMute, () => _player.ToggleMute());
        _shortcutController.RegisterAction(PlayerShortcutActions.VolumeUp, () => _player.AdjustVolume(5));
        _shortcutController.RegisterAction(PlayerShortcutActions.VolumeDown, () => _player.AdjustVolume(-5));
        _shortcutController.RegisterAction(PlayerShortcutActions.SeekToBeginning, () => SeekAbsolute(0));
        _shortcutController.RegisterAction(PlayerShortcutActions.ReturnToShell, () =>
        {
            if (_timelineController.IsScrubbing) _timelineController.CancelScrubbing();
            else if (_queueButton.Active) _queueButton.Active = false;
            else if (_commentsButton.Active) _commentsButton.Active = false;
            else if (_infoPanel.IsOpen) _infoPanel.Close();
            else ReturnToShell();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleQueue, () => _queueButton.Active = !_queueButton.Active);
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleVideoInfo, () => _infoPanel.Toggle(_session.CurrentVideo));
        _shortcutController.RegisterAction(PlayerShortcutActions.SpeedDecrease, () => AdjustSpeed(-1));
        _shortcutController.RegisterAction(PlayerShortcutActions.SpeedIncrease, () => AdjustSpeed(1));
        _shortcutController.RegisterAction(PlayerShortcutActions.NextVideo, () => _player.MovePlaylist(true));
        _shortcutController.RegisterAction(PlayerShortcutActions.PreviousVideo, () => _player.MovePlaylist(false));
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleFullscreen, ToggleFullscreen);
        _shortcutController.RegisterAction(PlayerShortcutActions.PreferredSubtitle, ShowPreferredSubtitle);
        _shortcutController.RegisterAction(PlayerShortcutActions.ResumeOrSkip, () =>
        {
            if (!_resumeController.TryResume())
                _sponsorBlockController.TrySkipManualSegment();
        });
        _shortcutController.UpdateBindings(_preferences.GetPreferences().Shortcuts);
        _shortcutController.Attach();
    }

    public new void Dispose()
    {
        _timelineController.Dispose();
        _infoPanel.Dispose();
        _subtitleController.Dispose();
        foreach (var feature in _features) feature.Dispose();

        _chapterOverlay.Dispose();
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        _chromeController.Dispose();
        _commentsView.Dispose();
        _queueService.Changed -= OnQueueChanged;
        _queueView.Dispose();
        _queueViewModel.Dispose();
        _disposed = true;
        _shortcutController.Dispose();

        if (_rendererReady)
        {
            _playerSurface.MakeCurrent();
            _player.ShutdownRenderer();
            _rendererReady = false;
        }

        _player.RenderRequested -= OnRenderRequested;
        _player.StateChanged -= OnStateChanged;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _session.VideoChanged -= OnSessionVideoChanged;
        _session.SessionEnded -= OnSessionEnded;
        _session.Failed -= OnSessionFailed;
        _session.Dispose();
        _player.Dispose();
        _desktopMedia.Dispose();
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

            SetControls(100, 1, NormalizeQuality(preferences.VideoQuality));
            SetLoading(true);
            _queueControls.SetVisible(request.Videos.Length > 1);
            _previousQueueButton.Sensitive = false;
            _nextQueueButton.Sensitive = request.Videos.Length > 1;
            _shortcutController.Attach();
            Widget.GrabFocus();
            if (_rendererReady) _player.Load(request, preferences, _session.CookieFilePath);
            return false;
        });

        return Task.FromResult("Opening embedded player.");
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _shortcutController.UpdateBindings(preferences.Shortcuts);
    }

    private void OnPlayerSurfaceRealize(object? sender, EventArgs args)
    {
        _playerSurface.MakeCurrent();
        if (_playerSurface.GetError() is not null)
        {
            OnPlaybackFailed(this, "Unable to create an OpenGL context for embedded playback.");
            return;
        }

        _player.InitializeRenderer();
        _rendererReady = true;
        _shortcutController.Attach();

        if (_session.Request is not null)
            _player.Load(_session.Request, _preferences.GetPreferences(), _session.CookieFilePath);
    }

    private void OnPlayerSurfaceUnrealize(object? sender, EventArgs args)
    {
        _playerSurface.MakeCurrent();
        _player.ShutdownRenderer();
        _rendererReady = false;
    }

    private bool OnPlayerSurfaceRender(GLArea sender, GLArea.RenderSignalArgs args)
    {
        if (_disposed || !_rendererReady) return false;
        _player.Render(_playerSurface.GetAllocatedWidth() * _playerSurface.GetScaleFactor(),
            _playerSurface.GetAllocatedHeight() * _playerSurface.GetScaleFactor());
        return true;
    }



    private void SeekAbsolute(double position, bool exact = true)
    {
        _timelineController.SeekAbsolute(position, exact);
    }

    private void CancelScrubbing()
    {
        _timelineController.CancelScrubbing();
    }

    private void SeekRelative(double offset)
    {
        _player.SeekRelative(offset);
    }

    private void AdjustSpeed(int direction)
    {
        _player.SetSpeed(Math.Clamp(SnapPlaybackSpeed(_speed) + direction * PlaybackSpeedIncrement,
            MinimumPlaybackSpeed, MaximumPlaybackSpeed));
    }

    private void ToggleFullscreen()
    {
        if (Widget.GetRoot() is Window window) window.Fullscreened = !window.Fullscreened;
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
        _queueButton.Active = false;
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
        _engagement.SubmitVote(VideoVote.Like);
    }

    private void OnDislikeButtonClicked(object? sender, EventArgs args)
    {
        _engagement.SubmitVote(VideoVote.Dislike);
    }

    private void OnPlayerChannelButtonClicked(object? sender, EventArgs args)
    {
        OpenCurrentChannel();
    }


    private void OpenCurrentChannel()
    {
        if (_session.CurrentVideo is { } video)
            _channelRequested(video);
    }

    private void OnSponsorBlockSkipButtonClicked(object? sender, EventArgs args)
    {
        if (_sponsorBlockController.TrySkipManualSegment())
            RegisterActivity();
    }

    private void OnResumeButtonClicked(object? sender, EventArgs args)
    {
        if (_resumeController.TryResume())
            RegisterActivity();
    }

    private void OnRestartButtonClicked(object? sender, EventArgs args)
    {
        if (_resumeController.TryRestart())
            RegisterActivity();
    }


    private void OnSubtitleButtonClicked(object? sender, EventArgs args)
    {
        ShowPreferredSubtitle();
    }

    private void ShowPreferredSubtitle()
    {
        _subtitleController.ShowPreferredSubtitle();
    }

    private void OnCommentsButtonToggled(object? sender, EventArgs args)
    {
        if (_commentsButton.Active)
            _commentsView.EnsureLoaded();
    }

    private void CloseComments()
    {
        _commentsButton.Active = false;
    }

    private void OnVolumeScaleValueChanged(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetVolume(_volumeScale.GetValue());
    }

    private void OnQualityDropdownNotify(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetQuality(QualityAt(_qualityDropdown.GetSelected()));
    }

    private void OnSpeedScaleValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls) return;

        var speed = SnapPlaybackSpeed(_speedScale.GetValue());
        if (Math.Abs(_speedScale.GetValue() - speed) > 0.0001)
        {
            _updatingControls = true;
            try
            {
                _speedScale.SetValue(speed);
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
        if (_disposed) return;

        _playerSurface.QueueRender();
    }

    private void OnStateChanged(object? sender, LibMpvPlaybackState state)
    {
        if (_disposed) return;
        _speed = state.Speed;
        _session.UpdatePlayback(state);

        SetLoading(state.IsLoading);
        _updatingControls = true;
        _subtitleController.UpdateTracks(state.SubtitleTracks, _updatingControls);
        try
        {
            _timelineController.UpdatePosition(state);
            _chapterOverlay.Update(state.Chapters, state.Duration);

            _playPauseButton.SetIconName(state is { HasMedia: true, IsPaused: false }
                ? "media-playback-pause-symbolic"
                : "media-playback-start-symbolic");
            _playPauseButton.SetTooltipText(state is { HasMedia: true, IsPaused: false }
                ? "Pause (Space or K)"
                : "Play (Space or K)");
            _volumeScale.SetValue(Math.Clamp(state.Volume, 0, 100));
            _volumeButton.SetIconName(VolumeIcon(state.Volume, state.IsMuted));
            var speed = SnapPlaybackSpeed(state.Speed);
            _speedScale.SetValue(speed);
            SetSpeedLabel(speed);
            if (_session.Request is not { } request || state.PlaylistIndex is < 0 or >= int.MaxValue ||
                state.PlaylistIndex >= request.Videos.Length) return;
            _queueViewModel.SetCurrentPlayingIndex(state.PlaylistIndex);
            _previousQueueButton.Sensitive = state.PlaylistIndex > 0;
            _nextQueueButton.Sensitive = state.PlaylistIndex < request.Videos.Length - 1;
            var video = request.Videos[state.PlaylistIndex];
            foreach (var feature in _features) feature.UpdatePlayback(state, video.Id);
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void OnSessionVideoChanged(VideoSummary video, int playlistIndex)
    {
        _infoPanel.SetVideo(video);
        _titleLabel.SetText(video.Title);
        _channelLabel.SetText(video.ChannelName);
        if (!string.Equals(_commentsVideoId, video.Id, StringComparison.Ordinal))
        {
            foreach (var feature in _features) feature.Load(video);
            _commentsVideoId = video.Id;
            _commentsView.SetVideo(video.Id);
            if (_commentsButton.Active)
                _commentsView.EnsureLoaded();
        }
    }

    private void OnPlaybackFailed(object? sender, string detail)
    {
        _session.Fail(detail);
    }

    private void OnSessionFailed(string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _infoPanel.Close();
        _titleLabel.SetText("Playback failed");
        SetLoading(false);
        _channelLabel.SetText($"Embedded playback failed: {detail}");
        _timelineController.Reset();
        _playPauseButton.SetIconName("media-playback-start-symbolic");
        foreach (var feature in _features) feature.Clear();

        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        _queueControls.SetVisible(false);
        _queueButton.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _player.Stop();
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        _session.EndSession();
    }

    private void OnSessionEnded()
    {
        _timelineController.Reset();
        _infoPanel.Close();
        _queueControls.SetVisible(false);
        _queueButton.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        foreach (var feature in _features) feature.Clear();

        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        _commentsButton.Active = false;
        SetLoading(false);
    }

    private void ResetTransport()
    {
        _timelineController.Reset();
        _playPauseButton.SetIconName("media-playback-start-symbolic");
    }

    private void SetLoading(bool loading)
    {
        _loadingIndicator.SetVisible(loading);
        _centerControls.SetVisible(!loading);
    }

    private void SetControls(double volume, double speed, string quality)
    {
        _updatingControls = true;
        try
        {
            _volumeScale.SetValue(volume);
            _volumeButton.SetIconName(VolumeIcon(volume, false));
            var normalizedSpeed = SnapPlaybackSpeed(speed);
            _speedScale.SetValue(normalizedSpeed);
            SetSpeedLabel(normalizedSpeed);
            _qualityDropdown.SetSelected(
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
        _speedLabel.SetText($"{speed:0.##}×");
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
            if (_disposed || _syncingQueue || _session.Request is null)
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
            _queueControls.SetVisible(newVideos.Length > 1);
            _previousQueueButton.Sensitive = _session.CurrentPlaylistIndex > 0;
            _nextQueueButton.Sensitive = _session.CurrentPlaylistIndex < newVideos.Length - 1;
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
}