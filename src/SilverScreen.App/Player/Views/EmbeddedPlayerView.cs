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
using SilverScreen.Queue;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Window = Gtk.Window;

namespace SilverScreen.Player.Views;

internal interface IEmbeddedPlayerPresenter
{
    Task<string> PresentAsync(PlaybackRequest request);
}

public partial class EmbeddedPlayerView : ViewBase<OverlaySplitView>, IEmbeddedPlayerPresenter, IDisposable
{
    private const double MinimumPlaybackSpeed = 0.25;
    private const double MaximumPlaybackSpeed = 4;
    private const double PlaybackSpeedIncrement = 0.25;

    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    private readonly Action _backRequested;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly PlayerChapterOverlay _chapterOverlay;
    private readonly PlayerChromeController _chromeController;
    private readonly CommentsView _commentsView;
    private readonly DesktopMediaIntegration _desktopMedia;
    private readonly PlayerEngagementController _engagement;
    private readonly VideoInfoPanelController _infoPanel;
    private readonly LibMpvPlayer _player;
    private readonly IPreferencesService _preferences;

    private readonly Action _presentRequested;
    private readonly IQueueService _queueService;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly PlayerResumeController _resumeController;
    private readonly PlaybackSession _session;
    private readonly PlayerShortcutController _shortcutController;
    private readonly PlayerSponsorBlockController _sponsorBlockController;
    private readonly PlayerSubtitleController _subtitleController;
    private readonly PlayerTimelineController _timelineController;

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
        comments_sidebar_host.Append(_commentsView.Widget);
        _queueService = dependencies.Queue;
        _queueViewModel = new QueueViewModel(dependencies.Queue, new EmbeddedPlayerPlaybackService(this));
        _queueView = new QueueView(_queueViewModel, dependencies.Thumbnails, dependencies.WatchProgress, CloseQueue,
            OnTrackJumpRequested);
        player_queue_sidebar_host.Append(_queueView.Widget);
        player_queue_button.BindProperty("active", player_queue_split_view, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        _queueService.Changed += OnQueueChanged;
        _player = new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!_disposed) action();
            return false;
        }));
        _timelineController = new PlayerTimelineController(player_timeline, player_timeline_overlay, player_scrub_cue,
            player_scrub_time_label, player_scrub_delta_label, player_scrub_chapter_label, player_position_label,
            player_duration_label,
            (pos, exact) => _player.SeekAbsolute(pos, exact), RegisterActivity);
        _engagement = new PlayerEngagementController(dependencies.VideoEngagement, dependencies.YouTubeRating,
            dependencies.Session, player_like_button, player_like_image, player_likes_label, player_dislike_button,
            player_dislike_image, player_dislikes_label);
        _chapterOverlay = new PlayerChapterOverlay(player_timeline_overlay, player_timeline,
            () => _timelineController.PlaybackPosition, pos => SeekAbsolute(pos), RegisterActivity);
        _sponsorBlockController = new PlayerSponsorBlockController(dependencies.SponsorBlock, _preferences,
            player_timeline,
            player_timeline_overlay, player_sponsorblock_revealer, player_sponsorblock_skip_button,
            player_sponsorblock_label, pos => SeekAbsolute(pos));
        _resumeController = new PlayerResumeController(_preferences, dependencies.WatchProgress,
            player_resume_revealer, player_resume_button, player_resume_label,
            player_restart_revealer, player_restart_button, player_restart_label,
            pos => SeekAbsolute(pos));
        _desktopMedia = new DesktopMediaIntegration(_player, presentRequested);
        _session = new PlaybackSession(dependencies.PlaybackCoordinator, _desktopMedia);
        _session.VideoChanged += OnSessionVideoChanged;
        _session.SessionEnded += OnSessionEnded;
        _session.Failed += OnSessionFailed;
        _infoPanel = new VideoInfoPanelController(dependencies.VideoDetails, _channelRequested, player_info_backdrop,
            player_info_cue_revealer, player_info_revealer, player_info_title_label, player_info_channel_label,
            player_info_stats_label, player_info_status_label, player_info_description_scroller,
            player_info_description, player_info_close_button, () =>
            {
                if (_session.HasMedia) player_surface.GrabFocus();
            });
        _subtitleController = new PlayerSubtitleController(_preferences, player_subtitle_dropdown,
            player_subtitle_model,
            player_subtitle_button, trackId => _player.SelectSubtitleTrack(trackId));
        _player.RenderRequested += OnRenderRequested;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        SetControls(100, 1, "Best");
        _chromeController = new PlayerChromeController(
            Widget,
            player_header_bar,
            player_center_controls,
            player_controls,
            () => player_volume_popover.GetVisible() || player_settings_popover.GetVisible() || _infoPanel.IsOpen,
            () => _chapterOverlay.Layout(),
            UpdatePointer);
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
            else if (player_queue_button.Active) player_queue_button.Active = false;
            else if (Widget.ShowSidebar) CloseComments();
            else if (_infoPanel.IsOpen) _infoPanel.Close();
            else ReturnToShell();
        });
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleQueue,
            () => player_queue_button.Active = !player_queue_button.Active);
        _shortcutController.RegisterAction(PlayerShortcutActions.ToggleVideoInfo,
            () => _infoPanel.Toggle(_session.CurrentVideo));
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
        _engagement.Dispose();
        _sponsorBlockController.Dispose();
        _resumeController.Dispose();

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
            player_surface.MakeCurrent();
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
        GC.SuppressFinalize(this);
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
            player_queue_controls.SetVisible(request.Videos.Length > 1);
            player_previous_queue_button.Sensitive = false;
            player_next_queue_button.Sensitive = request.Videos.Length > 1;
            _presentRequested();
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
        player_surface.MakeCurrent();
        if (player_surface.GetError() is not null)
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
        player_surface.MakeCurrent();
        _player.ShutdownRenderer();
        _rendererReady = false;
    }

    private bool OnPlayerSurfaceRender(GLArea sender, GLArea.RenderSignalArgs args)
    {
        if (_disposed || !_rendererReady) return false;
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

    private void ShowPreferredSubtitle()
    {
        _subtitleController.ShowPreferredSubtitle();
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
        if (_disposed) return;

        player_surface.QueueRender();
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
            var video = request.Videos[state.PlaylistIndex];
            _sponsorBlockController.UpdatePlayback(state, video.Id);
            _resumeController.UpdatePlayback(state, video.Id);
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
        _engagement.Load(video);
        _sponsorBlockController.Load(video);
        _resumeController.Load(video);
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
        player_title_label.SetText("Playback failed");
        SetLoading(false);
        player_channel_label.SetText($"Embedded playback failed: {detail}");
        _timelineController.Reset();
        player_play_pause_button.SetIconName("media-playback-start-symbolic");
        _engagement.Clear();
        _sponsorBlockController.Clear();
        _resumeController.Clear();

        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        CloseComments();
        player_comments_cue_revealer.RevealChild = false;
        player_queue_controls.SetVisible(false);
        player_queue_button.Active = false;
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
        player_queue_controls.SetVisible(false);
        player_queue_button.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _engagement.Clear();
        _sponsorBlockController.Clear();
        _resumeController.Clear();

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
}