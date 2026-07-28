using Adw;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Views.Comments;
using XSTH.Blueprint.Helpers;
using static Gdk.Constants;
using Functions = GLib.Functions;
using Window = Gtk.Window;

namespace SilverScreen.Views.Player;

internal interface IEmbeddedPlayerPresenter
{
    Task<string> PresentAsync(PlaybackRequest request);
}

public partial class EmbeddedPlayerView : ViewBase<OverlaySplitView>, IEmbeddedPlayerPresenter, IDisposable
{
    private const long ControlsIdleDelayMilliseconds = 1_500;
    private const int ChapterMarkerHitTargetWidth = 20;
    private const uint ControlsVisibilityCheckMilliseconds = 100;
    private const double MinimumPlaybackSpeed = 0.25;
    private const double MaximumPlaybackSpeed = 5;
    private const double PlaybackSpeedIncrement = 0.25;
    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    private readonly Action _backRequested;
    private readonly Box _centerControls;
    private readonly Label _channelLabel;
    private readonly ToggleButton _commentsButton;
    private readonly CommentsView _commentsView;
    private readonly List<Button> _chapterMarkers = [];
    private TimeSpan _chapterDuration;
    private int _chapterMarkerHostWidth = -1;
    private IReadOnlyList<LibMpvChapter> _chapters = [];
    private readonly Overlay _chapterMarkerHost;
    private readonly ICookieFileProvider _cookieFiles;
    private readonly DesktopMediaIntegration _desktopMedia;
    private readonly Button _dislikeButton;
    private readonly Image _dislikeImage;
    private readonly Label _dislikesLabel;
    private readonly Label _durationLabel;
    private readonly Widget _headerBar;
    private readonly Button _likeButton;
    private readonly Image _likeImage;
    private readonly Label _likesLabel;
    private readonly Box _loadingIndicator;
    private readonly Button _playPauseButton;
    private readonly IPlaybackPresenceService _playbackPresence;
    private readonly IYouTubePlaybackTelemetryService _playbackTelemetry;
    private readonly LibMpvPlayer _player;
    private readonly Widget _playerControls;
    private readonly GLArea _playerSurface;
    private readonly Label _positionLabel;
    private readonly IPreferencesService _preferences;
    private readonly Action _presentRequested;
    private readonly DropDown _qualityDropdown;
    private readonly Box _queueControls;
    private readonly ISessionService _session;
    private readonly Popover _settingsPopover;
    private readonly Dictionary<uint, Action> _shortcutMap = [];
    private readonly Label _speedLabel;
    private readonly Scale _speedScale;
    private readonly Button _subtitleButton;
    private readonly DropDown _subtitleDropdown;
    private readonly StringList _subtitleModel;
    private readonly Scale _timeline;
    private readonly Label _titleLabel;
    private readonly IVideoEngagementService _videoEngagement;
    private readonly MenuButton _volumeButton;
    private readonly Popover _volumePopover;
    private readonly Scale _volumeScale;
    private readonly IYouTubeRatingService _youtubeRating;
    private string? _commentsVideoId;
    private uint _controlsAutohideSource;
    private bool _controlsVisible = true;
    private CookieFileLease? _cookieFile;
    private bool _disposed;
    private CancellationTokenSource? _engagementCancellation;
    private long _engagementLoadVersion;
    private string? _engagementVideoId;
    private bool _hasMedia;
    private EventControllerKey? _keyboardController;
    private Widget? _keyboardRoot;
    private long _lastActivityMilliseconds;
    private double _lastPointerX = double.NaN;
    private double _lastPointerY = double.NaN;
    private IYouTubePlaybackTelemetrySession? _playbackTelemetrySession;
    private YouTubeRatingState _ratingState;
    private bool _rendererReady;
    private PlaybackRequest? _request;
    private double _speed = 1;
    private IReadOnlyList<LibMpvSubtitleTrack> _subtitleTracks = [];
    private bool _updatingControls;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, IPreferencesService preferences,
        ICookieFileProvider cookieFiles, IPlaybackPresenceService playbackPresence,
        IYouTubePlaybackTelemetryService playbackTelemetry, IVideoEngagementService videoEngagement,
        IYouTubeRatingService youtubeRating, ISessionService session, IYouTubeCommentService comments)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _preferences = preferences;
        _cookieFiles = cookieFiles;
        _playbackPresence = playbackPresence;
        _playbackTelemetry = playbackTelemetry;
        _videoEngagement = videoEngagement;
        _youtubeRating = youtubeRating;
        _session = session;
        _playerSurface = GetRequiredObject<GLArea>("player_surface");
        _headerBar = GetRequiredObject<Widget>("player_header_bar");
        _centerControls = GetRequiredObject<Box>("player_center_controls");
        _playerControls = GetRequiredObject<Widget>("player_controls");
        _playPauseButton = GetRequiredObject<Button>("player_play_pause_button");
        _volumeButton = GetRequiredObject<MenuButton>("player_volume_button");
        _volumeScale = GetRequiredObject<Scale>("player_volume_scale");
        _volumePopover = GetRequiredObject<Popover>("player_volume_popover");
        _settingsPopover = GetRequiredObject<Popover>("player_settings_popover");
        _qualityDropdown = GetRequiredObject<DropDown>("player_quality_dropdown");
        _queueControls = GetRequiredObject<Box>("player_queue_controls");
        _speedLabel = GetRequiredObject<Label>("player_speed_label");
        _speedScale = GetRequiredObject<Scale>("player_speed_scale");
        _subtitleDropdown = GetRequiredObject<DropDown>("player_subtitle_dropdown");
        _subtitleModel = GetRequiredObject<StringList>("player_subtitle_model");
        _timeline = GetRequiredObject<Scale>("player_timeline");
        _chapterMarkerHost = GetRequiredObject<Overlay>("player_timeline_overlay");
        _loadingIndicator = GetRequiredObject<Box>("player_loading_indicator");
        _titleLabel = GetRequiredObject<Label>("player_title_label");
        _channelLabel = GetRequiredObject<Label>("player_channel_label");
        _positionLabel = GetRequiredObject<Label>("player_position_label");
        _durationLabel = GetRequiredObject<Label>("player_duration_label");
        _likesLabel = GetRequiredObject<Label>("player_likes_label");
        _likeButton = GetRequiredObject<Button>("player_like_button");
        _likeImage = GetRequiredObject<Image>("player_like_image");
        _dislikeButton = GetRequiredObject<Button>("player_dislike_button");
        _dislikesLabel = GetRequiredObject<Label>("player_dislikes_label");
        _dislikeImage = GetRequiredObject<Image>("player_dislike_image");
        _subtitleButton = GetRequiredObject<Button>("player_subtitle_button");
        _commentsButton = GetRequiredObject<ToggleButton>("player_comments_button");
        var commentsSidebarHost = GetRequiredObject<Box>("comments_sidebar_host");
        _commentsView = new CommentsView(comments, CloseComments);
        commentsSidebarHost.Append(_commentsView.Widget);
        _commentsButton.BindProperty("active", Widget, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        SetReactionSensitive(false);
        _player = new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!_disposed) action();
            return false;
        }));
        _desktopMedia = new DesktopMediaIntegration(_player, _presentRequested);
        _player.RenderRequested += OnRenderRequested;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        SetControls(100, 1, "Best");
        SetupControlsAutohide();
        SetupKeyboardShortcuts();

        DeclareBindings();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        if (_controlsAutohideSource != 0) Functions.SourceRemove(_controlsAutohideSource);
        CancelEngagementLoad();
        _commentsView.Dispose();
        _disposed = true;
        if (_keyboardRoot is not null && _keyboardController is not null)
        {
            _keyboardRoot.RemoveController(_keyboardController);
            _keyboardRoot = null;
        }

        if (_rendererReady)
        {
            _playerSurface.MakeCurrent();
            _player.ShutdownRenderer();
            _rendererReady = false;
        }

        _player.RenderRequested -= OnRenderRequested;
        _player.StateChanged -= OnStateChanged;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _player.Dispose();
        _desktopMedia.Dispose();
        ReleaseSession();
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
            return Task.FromResult(exception.Message);
        }

        if (!_player.IsAvailable)
            return Task.FromResult(_player.AvailabilityError ?? RuntimeDependencyGuidance.LibMpvUnavailable);

        Functions.IdleAdd(0, () =>
        {
            if (_disposed) return false;
            EndSession(true);
            _request = request;
            _playbackTelemetrySession = _playbackTelemetry.Start(request);
            _cookieFile = _cookieFiles.CreateCookieFile();
            var preferences = _preferences.GetPreferences();
            var firstVideo = request.Videos[0];
            _titleLabel.SetText(firstVideo.Title);
            _channelLabel.SetText(firstVideo.ChannelName);
            _durationLabel.SetText(FormatTime(firstVideo.Duration));
            LoadEngagement(firstVideo);
            _commentsVideoId = firstVideo.Id;
            _commentsView.SetVideo(firstVideo.Id);
            RegisterActivity();
            UpdateChapters([], TimeSpan.Zero);
            SetControls(100, 1, NormalizeQuality(preferences.VideoQuality));
            SetLoading(true);
            _queueControls.SetVisible(request.Videos.Length > 1);
            _hasMedia = false;
            _presentRequested();
            AttachKeyboardShortcuts();
            Widget.GrabFocus();
            if (_rendererReady) _player.Load(request, preferences, _cookieFile?.Path);
            return false;
        });

        return Task.FromResult("Opening embedded player.");
    }

    private void DeclareBindings()
    {
        Bind(_player.TogglePause, KEY_space, KEY_K, KEY_k);
        Bind(() => _player.SeekRelative(-10), KEY_Left, KEY_J, KEY_j);
        Bind(() => _player.SeekRelative(10), KEY_Right, KEY_L, KEY_l);
        Bind(() => _player.StepFrame(false), KEY_comma, KEY_less);
        Bind(() => _player.StepFrame(true), KEY_period, KEY_greater);
        Bind(_player.ToggleMute, KEY_M, KEY_m);
        Bind(() => _player.AdjustVolume(5), KEY_Up);
        Bind(() => _player.AdjustVolume(-5), KEY_Down);
        Bind(() => _player.SeekAbsolute(0), KEY_0, KEY_Home);
        Bind(ReturnToShell, KEY_Escape);
        Bind(() => AdjustSpeed(-1), KEY_bracketleft, KEY_braceleft);
        Bind(() => AdjustSpeed(1), KEY_bracketright, KEY_braceright);
        Bind(() => _player.MovePlaylist(true), KEY_N, KEY_n);
        Bind(() => _player.MovePlaylist(false), KEY_P, KEY_p);
        Bind(ToggleFullscreen, KEY_F, KEY_f);
        Bind(ShowPreferredSubtitle, KEY_C, KEY_c);
        return;

        void Bind(Action action, params uint[] keys)
        {
            foreach (var key in keys)
                _shortcutMap[key] = action;
        }
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
        AttachKeyboardShortcuts();

        if (_request is not null)
            _player.Load(_request, _preferences.GetPreferences(), _cookieFile?.Path);
    }

    private void OnPlayerSurfaceUnrealize(object? sender, EventArgs args)
    {
        _playerSurface.MakeCurrent();
        _player.ShutdownRenderer();
        _rendererReady = false;
    }

    private bool OnPlayerSurfaceRender(object? sender, GLArea.RenderSignalArgs args)
    {
        if (_disposed || !_rendererReady) return false;
        _player.Render(_playerSurface.GetAllocatedWidth() * _playerSurface.GetScaleFactor(),
            _playerSurface.GetAllocatedHeight() * _playerSurface.GetScaleFactor());
        return true;
    }

    private void SetupControlsAutohide()
    {
        var motion = EventControllerMotion.New();
        motion.SetPropagationPhase(PropagationPhase.Capture);
        motion.OnMotion += (_, args) => RegisterPointerActivity(args.X, args.Y);
        Widget.AddController(motion);

        var click = GestureClick.New();
        click.Button = 0;
        click.SetPropagationPhase(PropagationPhase.Capture);
        click.OnPressed += (_, _) => RegisterActivity();
        Widget.AddController(click);

        RegisterActivity();
        _controlsAutohideSource = Functions.TimeoutAdd(0, ControlsVisibilityCheckMilliseconds, () =>
        {
            if (_disposed) return false;
            if (_controlsVisible &&
                !HasOpenControlPopover() &&
                Environment.TickCount64 - _lastActivityMilliseconds >= ControlsIdleDelayMilliseconds)
                SetControlsVisible(false);
            return true;
        });
    }

    private void SetupKeyboardShortcuts()
    {
        var key = EventControllerKey.New();
        key.SetPropagationPhase(PropagationPhase.Capture);
        key.OnKeyPressed += (_, args) => HandleKeyboardShortcut(args.Keyval);
        _keyboardController = key;
        AttachKeyboardShortcuts();
    }

    private void AttachKeyboardShortcuts()
    {
        if (_keyboardController is null || _keyboardRoot is not null) return;
        if (Widget.GetRoot() is not Widget root) return;

        root.AddController(_keyboardController);
        _keyboardRoot = root;
    }

    private bool HandleKeyboardShortcut(uint keyval)
    {
        if (!_hasMedia || !_shortcutMap.TryGetValue(keyval, out var action))
            return false;

        action();

        RegisterActivity();
        return true;
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
        _lastActivityMilliseconds = Environment.TickCount64;
        LayoutChapterMarkers(_chapterDuration);
        SetControlsVisible(true);
    }

    private void RegisterPointerActivity(double x, double y)
    {
        if (Math.Abs(x - _lastPointerX) < 0.2 && Math.Abs(y - _lastPointerY) < 0.2) return;
        _lastPointerX = x;
        _lastPointerY = y;
        RegisterActivity();
    }

    private bool HasOpenControlPopover()
    {
        return _volumePopover.GetVisible() || _settingsPopover.GetVisible();
    }


    private void SetControlsVisible(bool visible)
    {
        if (_controlsVisible == visible) return;
        _controlsVisible = visible;
        SetControlVisible(_headerBar, visible);
        SetControlVisible(_centerControls, visible);
        SetControlVisible(_playerControls, visible);
        if (!visible)
            Widget.GrabFocus();
    }

    private static void SetControlVisible(Widget control, bool visible)
    {
        control.SetSensitive(visible);
        if (visible)
            control.RemoveCssClass("player-chrome-hidden");
        else
            control.AddCssClass("player-chrome-hidden");
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


    private void OnRewindButtonClicked(object? sender, EventArgs args)
    {
        _player.SeekRelative(-10);
    }

    private void OnForwardButtonClicked(object? sender, EventArgs args)
    {
        _player.SeekRelative(10);
    }

    private void OnPlayPauseButtonClicked(object? sender, EventArgs args)
    {
        _player.TogglePause();
    }

    private void OnLikeButtonClicked(object? sender, EventArgs args)
    {
        SubmitVote(VideoVote.Like);
    }

    private void OnDislikeButtonClicked(object? sender, EventArgs args)
    {
        SubmitVote(VideoVote.Dislike);
    }

    private void OnSubtitleButtonClicked(object? sender, EventArgs args)
    {
        ShowPreferredSubtitle();
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
        var selected = _subtitleDropdown.GetSelected();
        if (selected is 0 or > int.MaxValue || selected > _subtitleTracks.Count)
        {
            _player.SelectSubtitleTrack(0);
            return;
        }

        var track = _subtitleTracks[(int)selected - 1];
        _player.SelectSubtitleTrack(track.Id);
        SavePreferredSubtitle(track.Language);
    }


    private void OnTimelineValueChanged(object? sender, EventArgs args)
    {
        if (!_updatingControls && _timeline.GetSensitive()) _player.SeekAbsolute(_timeline.GetValue());
    }

    private void OnRenderRequested(object? sender, EventArgs args)
    {
        if (!_disposed) _playerSurface.QueueRender();
    }

    private void OnStateChanged(object? sender, LibMpvPlaybackState state)
    {
        if (_disposed) return;
        _hasMedia = state.HasMedia;
        _speed = state.Speed;
        if (_request is { } playbackRequest && state.HasMedia)
        {
            var playbackState = new PlaybackPresenceState(state.PlaylistIndex, state.Position,
                state.Duration, state.IsPaused, state.Speed, DateTimeOffset.UtcNow);
            _playbackPresence.SetPlaybackState(playbackRequest, playbackState);
            _playbackTelemetrySession?.UpdateState(playbackState);
        }

        _desktopMedia.UpdatePlayback(_request, state);
        SetLoading(state.IsLoading);
        _updatingControls = true;
        UpdateSubtitleTracks(state.SubtitleTracks);
        try
        {
            _positionLabel.SetText(FormatTime(state.Position));
            _durationLabel.SetText(state.Duration == TimeSpan.Zero ? "Live" : FormatTime(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            UpdateChapters(state.Chapters, state.Duration);
            _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0, Math.Max(0, state.Duration.TotalSeconds)));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);
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
            if (_request is not { } request || state.PlaylistIndex is < 0 or >= int.MaxValue ||
                state.PlaylistIndex >= request.Videos.Length) return;
            var video = request.Videos[state.PlaylistIndex];
            _titleLabel.SetText(video.Title);
            _channelLabel.SetText(video.ChannelName);
            if (!string.Equals(_engagementVideoId, video.Id, StringComparison.Ordinal))
                LoadEngagement(video);

            if (string.Equals(_commentsVideoId, video.Id, StringComparison.Ordinal)) return;
            _commentsVideoId = video.Id;
            _commentsView.SetVideo(video.Id);
            if (_commentsButton.Active)
                _commentsView.EnsureLoaded();
        }
        finally
        {
            _updatingControls = false;
        }
    }


    private void OnPlaybackFailed(object? sender, string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _titleLabel.SetText("Playback failed");
        SetLoading(false);
        _channelLabel.SetText($"Embedded playback failed: {detail}");
        ResetTransport();
        CancelEngagementLoad();
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        _request = null;
        _queueControls.SetVisible(false);
        _hasMedia = false;
        _player.Stop();
        ReleaseSession();
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        ReleaseSession();
        _request = null;
        _hasMedia = false;
        _queueControls.SetVisible(false);
        CancelEngagementLoad();
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        _commentsButton.Active = false;
        SetLoading(false);
    }

    private void ReleaseSession()
    {
        _playbackPresence.Clear();
        _playbackTelemetrySession?.Dispose();
        _playbackTelemetrySession = null;
        _cookieFile?.Dispose();
        _cookieFile = null;
        _desktopMedia.ClearPlayback();
    }

    private void ResetTransport()
    {
        _updatingControls = true;
        try
        {
            _timeline.SetRange(0, 0);
            _timeline.SetValue(0);
            _timeline.SetSensitive(false);
            _playPauseButton.SetIconName("media-playback-start-symbolic");
            _positionLabel.SetText("0:00");
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void UpdateChapters(IReadOnlyList<LibMpvChapter> chapters, TimeSpan duration)
    {
        if (!_chapters.SequenceEqual(chapters))
        {
            foreach (var marker in _chapterMarkers)
                _chapterMarkerHost.RemoveOverlay(marker);

            _chapterMarkers.Clear();
            _chapters = chapters;
            _chapterMarkerHostWidth = -1;
            foreach (var chapter in chapters)
            {
                var marker = Button.New();
                marker.AddCssClass("player-chapter-marker");
                marker.SetChild(CreateChapterMarkerLine());
                marker.SetTooltipText(chapter.Title);
                marker.OnClicked += (_, _) =>
                {
                    _player.SeekAbsolute(chapter.Start.TotalSeconds);
                    RegisterActivity();
                };
                marker.Halign = Align.Start;
                marker.Valign = Align.Center;
                _chapterMarkerHost.AddOverlay(marker);
                _chapterMarkers.Add(marker);
            }
        }

        LayoutChapterMarkers(duration);
    }

    private static Box CreateChapterMarkerLine()
    {
        var line = Box.New(Orientation.Vertical, 0);
        line.AddCssClass("player-chapter-marker-line");
        line.Halign = Align.Center;
        line.Valign = Align.Center;
        return line;
    }

    private void LayoutChapterMarkers(TimeSpan duration)
    {
        var width = _chapterMarkerHost.GetAllocatedWidth();
        if (_chapterDuration == duration && _chapterMarkerHostWidth == width) return;

        _chapterDuration = duration;
        _chapterMarkerHostWidth = width;
        var hasDuration = duration > TimeSpan.Zero;
        for (var index = 0; index < _chapterMarkers.Count; index++)
        {
            var marker = _chapterMarkers[index];
            var chapter = _chapters[index];
            marker.SetVisible(hasDuration && chapter.Start <= duration);
            if (!hasDuration) continue;

            var position = Math.Clamp(chapter.Start.TotalSeconds / duration.TotalSeconds, 0, 1);
            var markerX = Math.Clamp(Math.Round(position * (width - 1) - ChapterMarkerHitTargetWidth / 2d),
                0, Math.Max(0, width - ChapterMarkerHitTargetWidth));
            marker.MarginStart = (int)markerX;
        }
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

    private void UpdateSubtitleTracks(IReadOnlyList<LibMpvSubtitleTrack> tracks)
    {
        if (_subtitleTracks.SequenceEqual(tracks))
        {
            UpdateSubtitleButton();
            return;
        }

        _subtitleTracks = tracks;
        while (_subtitleModel.GetNItems() > 0) _subtitleModel.Remove(0);
        _subtitleModel.Append("Off");
        uint selected = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            _subtitleModel.Append(track.Label);
            if (track.IsSelected) selected = (uint)index + 1;
        }

        _subtitleDropdown.SetSelected(selected);
        UpdateSubtitleButton();
    }

    private void ShowPreferredSubtitle()
    {
        var preferredLanguage = _preferences.GetPreferences().PreferredSubtitleLanguage;
        var track = _subtitleTracks.FirstOrDefault(track =>
            SubtitleLanguageMatches(track.Language, preferredLanguage));
        if (track is not null) _player.SelectSubtitleTrack(track.IsSelected ? 0 : track.Id);
    }

    private void SavePreferredSubtitle(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        var preferences = _preferences.GetPreferences();
        if (string.Equals(preferences.PreferredSubtitleLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSubtitleButton();
            return;
        }

        preferences.PreferredSubtitleLanguage = language;
        try
        {
            _preferences.SavePreferences(preferences);
            UpdateSubtitleButton();
        }
        catch (PreferencesPersistenceException exception)
        {
            Logger.Warning(exception, "Could not save preferred subtitle language");
        }
    }

    private void UpdateSubtitleButton()
    {
        var preferredLanguage = _preferences.GetPreferences().PreferredSubtitleLanguage;
        var track = _subtitleTracks.FirstOrDefault(track =>
            SubtitleLanguageMatches(track.Language, preferredLanguage));
        _subtitleButton.SetSensitive(track is not null);
        _subtitleButton.SetTooltipText(track is null
            ? "Choose a subtitle in player settings to set your preference"
            : track.IsSelected
                ? "Turn off preferred subtitles (C)"
                : $"Use preferred subtitles: {preferredLanguage} (C)");
    }

    private static bool SubtitleLanguageMatches(string language, string preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(preferredLanguage)) return false;
        if (string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase)) return true;

        var languageSeparator = language.IndexOf('-');
        var preferredLanguageSeparator = preferredLanguage.IndexOf('-');
        return language.AsSpan(0, languageSeparator < 0 ? language.Length : languageSeparator)
            .Equals(preferredLanguage.AsSpan(0,
                    preferredLanguageSeparator < 0 ? preferredLanguage.Length : preferredLanguageSeparator),
                StringComparison.OrdinalIgnoreCase);
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

    private void LoadEngagement(VideoSummary video)
    {
        CancelEngagementLoad();
        _engagementVideoId = video.Id;
        _likesLabel.SetText("—");
        _dislikesLabel.SetText("—");
        SetRatingState(YouTubeRatingState.None);
        var hasNativeRatingSession = _session.GetCurrentSession().IsSignedIn;
        SetReactionSensitive(PlaybackRequest.LooksLikeYouTubeVideoId(video.Id) && hasNativeRatingSession);
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(video.Id)) return;

        var cancellation = new CancellationTokenSource();
        _engagementCancellation = cancellation;
        _ = UpdateEngagementAsync(video.Id, _engagementLoadVersion, cancellation.Token);
        _ = UpdateRatingStateAsync(video.Id, _engagementLoadVersion, cancellation.Token);
    }

    private async Task UpdateEngagementAsync(string videoId, long version, CancellationToken cancellationToken)
    {
        VideoEngagement? engagement;
        try
        {
            engagement = await _videoEngagement.GetEngagementAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to load engagement counts for {VideoId}", videoId);
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested || version != _engagementLoadVersion)
                return false;

            _likesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Likes));
            _dislikesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Dislikes));
            return false;
        });
    }

    private async Task UpdateRatingStateAsync(string videoId, long version, CancellationToken cancellationToken)
    {
        YouTubeRatingState ratingState;
        try
        {
            ratingState = await _youtubeRating.GetRatingStateAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to load the native YouTube rating for {VideoId}", videoId);
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested || version != _engagementLoadVersion)
                return false;

            SetRatingState(ratingState);
            return false;
        });
    }

    private void SubmitVote(VideoVote vote)
    {
        if (_engagementVideoId is not { } videoId || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return;

        var removeVote =
            _ratingState == (vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike);
        SetReactionSensitive(false);
        _ = SubmitVoteAsync(videoId, vote, removeVote, _engagementLoadVersion);
    }

    private async Task SubmitVoteAsync(string videoId, VideoVote vote, bool removeVote, long version)
    {
        var succeeded = false;
        try
        {
            succeeded = removeVote
                ? await _youtubeRating.RemoveVoteAsync(videoId, vote).ConfigureAwait(false)
                : await _youtubeRating.SubmitVoteAsync(videoId, vote).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to submit {Vote} for {VideoId}", vote, videoId);
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || version != _engagementLoadVersion || _engagementVideoId != videoId) return false;

            SetReactionSensitive(true);
            if (!succeeded) return false;

            SetRatingState(removeVote
                ? YouTubeRatingState.None
                : vote == VideoVote.Like
                    ? YouTubeRatingState.Like
                    : YouTubeRatingState.Dislike);

            _ = UpdateEngagementAsync(videoId, version, CancellationToken.None);

            return false;
        });
    }

    private void SetReactionSensitive(bool sensitive)
    {
        _likeButton.SetSensitive(sensitive);
        _dislikeButton.SetSensitive(sensitive);
    }

    private void SetRatingState(YouTubeRatingState ratingState)
    {
        _ratingState = ratingState;
        _likeImage.SetFromResource(ratingState == YouTubeRatingState.Like
            ? "/SilverScreen/Assets/liked-symbolic.svg"
            : "/SilverScreen/Assets/like-symbolic.svg");
        _dislikeImage.SetFromResource(ratingState == YouTubeRatingState.Dislike
            ? "/SilverScreen/Assets/disliked-symbolic.svg"
            : "/SilverScreen/Assets/dislike-symbolic.svg");
    }

    private void CancelEngagementLoad()
    {
        _engagementLoadVersion++;
        _engagementCancellation?.Cancel();
        _engagementCancellation?.Dispose();
        _engagementCancellation = null;
        _engagementVideoId = null;
        SetReactionSensitive(false);
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }

    private static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (long)Math.Floor(value.TotalSeconds));
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}