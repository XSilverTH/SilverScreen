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
    private const long ControlsIdleDelayMilliseconds = 1_500;
    private const uint ControlsVisibilityCheckMilliseconds = 100;
    private const double MinimumPlaybackSpeed = 0.25;
    private const double MaximumPlaybackSpeed = 5;
    private const double PlaybackSpeedIncrement = 0.25;

    private const uint SeekThrottleIntervalMilliseconds = 120;
    private const long ReconciliationLatchMilliseconds = 400;
    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    private readonly Action _backRequested;
    private readonly Box _centerControls;
    private readonly Label _channelLabel;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly PlayerChapterOverlay _chapterOverlay;
    private readonly ToggleButton _commentsButton;
    private readonly CommentsView _commentsView;
    private readonly ICookieFileProvider _cookieFiles;
    private readonly DesktopMediaIntegration _desktopMedia;
    private readonly Label _durationLabel;
    private readonly PlayerEngagementController _engagement;
    private readonly ImmutableArray<IPlayerFeature> _features;
    private readonly Widget _headerBar;
    private readonly Button _infoBackdrop;
    private readonly Label _infoChannelLabel;
    private readonly Button _infoCloseButton;
    private readonly Button _infoCueButton;
    private readonly TextView _infoDescription;
    private readonly ScrolledWindow _infoDescriptionScroller;
    private readonly Revealer _infoRevealer;
    private readonly Label _infoStatsLabel;
    private readonly Label _infoStatusLabel;
    private readonly Label _infoTitleLabel;
    private readonly Box _loadingIndicator;
    private readonly Button _nextQueueButton;
    private readonly Button _playPauseButton;
    private readonly IPlaybackPresenceService _playbackPresence;
    private readonly IYouTubePlaybackTelemetryService _playbackTelemetry;
    private readonly LibMpvPlayer _player;
    private readonly Widget _playerControls;
    private readonly GLArea _playerSurface;
    private readonly Label _positionLabel;
    private readonly IPreferencesService _preferences;
    private readonly Action _presentRequested;
    private readonly Button _previousQueueButton;
    private readonly DropDown _qualityDropdown;
    private readonly ToggleButton _queueButton;
    private readonly Box _queueControls;
    private readonly IQueueService _queueService;
    private readonly QueueView _queueView;
    private readonly QueueViewModel _queueViewModel;
    private readonly PlayerResumeController _resumeController;
    private readonly Label _scrubChapterLabel;
    private readonly Box _scrubCue;
    private readonly Label _scrubDeltaLabel;
    private readonly Label _scrubTimeLabel;
    private readonly Popover _settingsPopover;
    private readonly Dictionary<uint, PlayerShortcutAction> _shortcutMap = [];
    private readonly Label _speedLabel;
    private readonly Scale _speedScale;
    private readonly PlayerSponsorBlockController _sponsorBlockController;

    private readonly PlayerSubtitleController _subtitleController;
    private readonly Scale _timeline;
    private readonly GestureDrag _timelineDragGesture;
    private readonly EventControllerMotion _timelineMotionController;
    private readonly Overlay _timelineOverlay;
    private readonly Label _titleLabel;
    private readonly IYouTubeVideoDetailsService _videoDetails;
    private readonly MenuButton _volumeButton;
    private readonly Popover _volumePopover;
    private readonly Scale _volumeScale;
    private readonly IWatchProgressService _watchProgress;
    private bool _bottomEdgeActive;
    private IReadOnlyList<LibMpvChapter> _chapters = [];
    private string? _commentsVideoId;
    private bool _controlsVisible = true;
    private CookieFileLease? _cookieFile;
    private TimeSpan _currentDuration;
    private int _currentPlaylistIndex = -1;
    private VideoSummary? _currentVideo;
    private bool _disposed;
    private bool _hasMedia;

    private CancellationTokenSource? _infoLoadCancellation;
    private int _infoLoadGeneration;
    private bool _infoOpen;
    private bool _isScrubbing;
    private EventControllerKey? _keyboardController;
    private Widget? _keyboardRoot;
    private long _lastActivityMilliseconds;
    private double _lastPointerX = double.NaN;
    private double _lastPointerY = double.NaN;
    private long _lastThrottledSeekTime;
    private double _latestScrubPosition;
    private double _pendingSeekTarget = -1;
    private IYouTubePlaybackTelemetrySession? _playbackTelemetrySession;
    private long _reconciliationLatchExpiry;
    private bool _rendererReady;
    private PlaybackRequest? _request;
    private TimeSpan _scrubStartPosition;
    private PlayerShortcutBindings _shortcuts = new();
    private double _speed = 1;
    private bool _syncingQueue;
    private uint _throttledSeekSource;
    private TimeSpan _timelinePlaybackPosition;
    private bool _updatingControls;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, Action<VideoSummary> channelRequested,
        PlayerDependencies dependencies)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _channelRequested = channelRequested;
        _preferences = dependencies.Preferences;
        _cookieFiles = dependencies.CookieFiles;
        _playbackPresence = dependencies.PlaybackPresence;
        _playbackTelemetry = dependencies.PlaybackTelemetry;
        _watchProgress = dependencies.WatchProgress;
        _videoDetails = dependencies.VideoDetails;
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
        var subtitleDropdown = GetRequiredObject<DropDown>("player_subtitle_dropdown");
        var subtitleModel = GetRequiredObject<StringList>("player_subtitle_model");
        _timeline = GetRequiredObject<Scale>("player_timeline");
        _timelineOverlay = GetRequiredObject<Overlay>("player_timeline_overlay");
        _scrubCue = GetRequiredObject<Box>("player_scrub_cue");
        _scrubTimeLabel = GetRequiredObject<Label>("player_scrub_time_label");
        _scrubDeltaLabel = GetRequiredObject<Label>("player_scrub_delta_label");
        _scrubChapterLabel = GetRequiredObject<Label>("player_scrub_chapter_label");
        var sponsorBlockSkipButton = GetRequiredObject<Button>("player_sponsorblock_skip_button");
        var resumeButton = GetRequiredObject<Button>("player_resume_button");
        var restartButton = GetRequiredObject<Button>("player_restart_button");

        _timelineMotionController = EventControllerMotion.New();
        _timelineMotionController.OnMotion += OnTimelineMotion;
        _timelineMotionController.OnLeave += OnTimelineLeave;
        _timelineOverlay.AddController(_timelineMotionController);

        _timelineDragGesture = GestureDrag.New();
        _timelineDragGesture.Button = 1;
        _timelineDragGesture.SetPropagationPhase(PropagationPhase.Capture);
        _timelineDragGesture.OnDragBegin += OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate += OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd += OnTimelineDragEnd;
        _timeline.AddController(_timelineDragGesture);

        _loadingIndicator = GetRequiredObject<Box>("player_loading_indicator");
        _titleLabel = GetRequiredObject<Label>("player_title_label");
        _channelLabel = GetRequiredObject<Label>("player_channel_label");
        _positionLabel = GetRequiredObject<Label>("player_position_label");
        _durationLabel = GetRequiredObject<Label>("player_duration_label");
        _infoTitleLabel = GetRequiredObject<Label>("player_info_title_label");
        _infoCloseButton = GetRequiredObject<Button>("player_info_close_button");
        _infoCueButton = GetRequiredObject<Button>("player_info_cue_button");
        _infoBackdrop = GetRequiredObject<Button>("player_info_backdrop");
        _infoChannelLabel = GetRequiredObject<Label>("player_info_channel_label");
        _infoStatsLabel = GetRequiredObject<Label>("player_info_stats_label");
        _infoStatusLabel = GetRequiredObject<Label>("player_info_status_label");
        _infoDescription = GetRequiredObject<TextView>("player_info_description");
        _infoDescriptionScroller = GetRequiredObject<ScrolledWindow>("player_info_description_scroller");
        _infoRevealer = GetRequiredObject<Revealer>("player_info_revealer");
        var likesLabel = GetRequiredObject<Label>("player_likes_label");
        var likeButton = GetRequiredObject<Button>("player_like_button");
        var likeImage = GetRequiredObject<Image>("player_like_image");
        var dislikeButton = GetRequiredObject<Button>("player_dislike_button");
        var dislikesLabel = GetRequiredObject<Label>("player_dislikes_label");
        var dislikeImage = GetRequiredObject<Image>("player_dislike_image");
        var subtitleButton = GetRequiredObject<Button>("player_subtitle_button");
        _commentsButton = GetRequiredObject<ToggleButton>("player_comments_button");
        var commentsSidebarHost = GetRequiredObject<Box>("comments_sidebar_host");
        _commentsView = new CommentsView(new CommentsViewModel(dependencies.Comments), CloseComments);
        commentsSidebarHost.Append(_commentsView.Widget);
        _commentsButton.BindProperty("active", Widget, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        var queueSplitView = GetRequiredObject<OverlaySplitView>("player_queue_split_view");
        _queueButton = GetRequiredObject<ToggleButton>("player_queue_button");
        _previousQueueButton = GetRequiredObject<Button>("player_previous_queue_button");
        _nextQueueButton = GetRequiredObject<Button>("player_next_queue_button");
        var playerQueueSidebarHost = GetRequiredObject<Box>("player_queue_sidebar_host");
        _queueService = dependencies.Queue;
        _queueViewModel = new QueueViewModel(dependencies.Queue, new EmbeddedPlayerPlaybackService(this));
        _queueView = new QueueView(_queueViewModel, dependencies.Thumbnails, dependencies.WatchProgress, CloseQueue,
            OnTrackJumpRequested);
        playerQueueSidebarHost.Append(_queueView.Widget);
        _queueButton.BindProperty("active", queueSplitView, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
        _queueService.Changed += OnQueueChanged;
        _engagement = new PlayerEngagementController(dependencies.VideoEngagement, dependencies.YouTubeRating,
            dependencies.Session, likeButton, likeImage, likesLabel, dislikeButton, dislikeImage, dislikesLabel);
        _chapterOverlay = new PlayerChapterOverlay(_timelineOverlay, _timeline,
            () => _timelinePlaybackPosition, pos => SeekAbsolute(pos), RegisterActivity);
        _sponsorBlockController = new PlayerSponsorBlockController(dependencies.SponsorBlock, _preferences, _timeline,
            _timelineOverlay, sponsorBlockSkipButton, pos => SeekAbsolute(pos));
        _resumeController = new PlayerResumeController(_preferences, _watchProgress, resumeButton, restartButton,
            pos => SeekAbsolute(pos));
        _features = [_engagement, _sponsorBlockController, _resumeController];
        _player = new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!_disposed) action();
            return false;
        }));
        _desktopMedia = new DesktopMediaIntegration(_player, _presentRequested);
        _subtitleController = new PlayerSubtitleController(_preferences, subtitleDropdown, subtitleModel,
            subtitleButton, trackId => _player.SelectSubtitleTrack(trackId));
        _player.RenderRequested += OnRenderRequested;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        SetControls(100, 1, "Best");
        SetInfoContent(null);
        SetupControlsAutohide();
        SetupKeyboardShortcuts();
        _preferences.PreferencesChanged += OnPreferencesChanged;

        DeclareBindings();
    }

    public new void Dispose()
    {
        CancelThrottledSeek();
        _timelineMotionController.OnMotion -= OnTimelineMotion;
        _timelineMotionController.OnLeave -= OnTimelineLeave;
        _timelineOverlay.RemoveController(_timelineMotionController);
        _timelineMotionController.Dispose();

        _timelineDragGesture.OnDragBegin -= OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate -= OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd -= OnTimelineDragEnd;
        _timeline.RemoveController(_timelineDragGesture);
        _timelineDragGesture.Dispose();

        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        _subtitleController.Dispose();
        foreach (var feature in _features) feature.Dispose();

        _chapterOverlay.Dispose();
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        _commentsView.Dispose();
        _queueService.Changed -= OnQueueChanged;
        _queueView.Dispose();
        _queueViewModel.Dispose();
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
            _request = request;
            _syncingQueue = true;
            try
            {
                _queueService.Replace(request.Videos);
            }
            finally
            {
                _syncingQueue = false;
            }

            _currentPlaylistIndex = 0;
            _queueViewModel.SetCurrentPlayingIndex(0);
            _playbackTelemetrySession = _playbackTelemetry.Start(request);
            _cookieFile = _cookieFiles.CreateCookieFile();
            var preferences = _preferences.GetPreferences();
            var firstVideo = request.Videos[0];
            _currentVideo = firstVideo;
            _titleLabel.SetText(firstVideo.Title);
            _channelLabel.SetText(firstVideo.ChannelName);
            _durationLabel.SetText(FormatTime(firstVideo.Duration));
            foreach (var feature in _features) feature.Load(firstVideo);
            _commentsVideoId = firstVideo.Id;
            _commentsView.SetVideo(firstVideo.Id);
            RegisterActivity();
            _chapterOverlay.Update([], TimeSpan.Zero);

            SetControls(100, 1, NormalizeQuality(preferences.VideoQuality));
            SetLoading(true);
            _queueControls.SetVisible(request.Videos.Length > 1);
            _previousQueueButton.Sensitive = false;
            _nextQueueButton.Sensitive = request.Videos.Length > 1;
            _hasMedia = false;
            _presentRequested();
            AttachKeyboardShortcuts();
            Widget.GrabFocus();
            if (_rendererReady) _player.Load(request, preferences, _cookieFile?.Path);
            return false;
        });

        return Task.FromResult("Opening embedded player.");
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _shortcuts = preferences.Shortcuts;
        DeclareBindings();
    }

    private void DeclareBindings()
    {
        _shortcuts = _preferences.GetPreferences().Shortcuts;
        _shortcutMap.Clear();
        Bind(PlayerShortcutAction.TogglePause, _shortcuts.TogglePause);
        Bind(PlayerShortcutAction.SeekBackward, _shortcuts.SeekBackward);
        Bind(PlayerShortcutAction.SeekForward, _shortcuts.SeekForward);
        Bind(PlayerShortcutAction.StepFrameBackward, _shortcuts.StepFrameBackward);
        Bind(PlayerShortcutAction.StepFrameForward, _shortcuts.StepFrameForward);
        Bind(PlayerShortcutAction.ToggleMute, _shortcuts.ToggleMute);
        Bind(PlayerShortcutAction.VolumeUp, _shortcuts.VolumeUp);
        Bind(PlayerShortcutAction.VolumeDown, _shortcuts.VolumeDown);
        Bind(PlayerShortcutAction.SeekToBeginning, _shortcuts.SeekToBeginning);
        Bind(PlayerShortcutAction.ReturnToShell, _shortcuts.ReturnToShell);
        Bind(PlayerShortcutAction.ToggleVideoInfo, _shortcuts.ToggleVideoInfo);
        Bind(PlayerShortcutAction.SpeedDecrease, _shortcuts.SpeedDecrease);
        Bind(PlayerShortcutAction.SpeedIncrease, _shortcuts.SpeedIncrease);
        Bind(PlayerShortcutAction.NextVideo, _shortcuts.NextVideo);
        Bind(PlayerShortcutAction.PreviousVideo, _shortcuts.PreviousVideo);
        Bind(PlayerShortcutAction.ToggleFullscreen, _shortcuts.ToggleFullscreen);
        Bind(PlayerShortcutAction.PreferredSubtitle, _shortcuts.PreferredSubtitle);
        Bind(PlayerShortcutAction.ResumeOrSkip, _shortcuts.ResumeOrSkip);
        Bind(PlayerShortcutAction.ToggleQueue, _shortcuts.ToggleQueue);
    }

    private void Bind(PlayerShortcutAction action, IEnumerable<string> keyNames)
    {
        foreach (var keyName in keyNames)
        {
            if (string.IsNullOrWhiteSpace(keyName)) continue;
            var keyval = Gdk.Functions.KeyvalFromName(keyName.Trim());
            if (keyval == 0) continue;

            _shortcutMap[Gdk.Functions.KeyvalToLower(keyval)] = action;
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

    private bool OnPlayerSurfaceRender(GLArea sender, GLArea.RenderSignalArgs args)
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
        Functions.TimeoutAdd(0, ControlsVisibilityCheckMilliseconds, () =>
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
        keyval = Gdk.Functions.KeyvalToLower(keyval);
        if (!_hasMedia || !_shortcutMap.TryGetValue(keyval, out var action)) return false;

        if (action == PlayerShortcutAction.ResumeOrSkip)
        {
            if (!_resumeController.TryResume() && !_sponsorBlockController.TrySkipManualSegment())
                return false;
        }
        else
        {
            switch (action)
            {
                case PlayerShortcutAction.TogglePause:
                    _player.TogglePause();
                    break;
                case PlayerShortcutAction.SeekBackward:
                    SeekRelative(-10);
                    break;
                case PlayerShortcutAction.SeekForward:
                    SeekRelative(10);
                    break;
                case PlayerShortcutAction.StepFrameBackward:
                    _player.StepFrame(false);
                    break;
                case PlayerShortcutAction.StepFrameForward:
                    _player.StepFrame(true);
                    break;
                case PlayerShortcutAction.ToggleMute:
                    _player.ToggleMute();
                    break;
                case PlayerShortcutAction.VolumeUp:
                    _player.AdjustVolume(5);
                    break;
                case PlayerShortcutAction.VolumeDown:
                    _player.AdjustVolume(-5);
                    break;
                case PlayerShortcutAction.SeekToBeginning:
                    SeekAbsolute(0);
                    break;
                case PlayerShortcutAction.ReturnToShell:
                    if (_isScrubbing) CancelScrubbing();
                    else if (_queueButton.Active) _queueButton.Active = false;
                    else if (_commentsButton.Active) _commentsButton.Active = false;
                    else if (_infoOpen) CloseVideoInfo();
                    else ReturnToShell();
                    break;
                case PlayerShortcutAction.ToggleQueue:
                    _queueButton.Active = !_queueButton.Active;
                    break;
                case PlayerShortcutAction.ToggleVideoInfo:
                    ToggleVideoInfo();
                    break;
                case PlayerShortcutAction.SpeedDecrease:
                    AdjustSpeed(-1);
                    break;
                case PlayerShortcutAction.SpeedIncrease:
                    AdjustSpeed(1);
                    break;
                case PlayerShortcutAction.NextVideo:
                    _player.MovePlaylist(true);
                    break;
                case PlayerShortcutAction.PreviousVideo:
                    _player.MovePlaylist(false);
                    break;
                case PlayerShortcutAction.ToggleFullscreen:
                    ToggleFullscreen();
                    break;
                case PlayerShortcutAction.PreferredSubtitle:
                    ShowPreferredSubtitle();
                    break;
                case PlayerShortcutAction.ResumeOrSkip:
                    break;
            }
        }

        RegisterActivity();
        return true;
    }

    private void SeekAbsolute(double position, bool exact = true)
    {
        _pendingSeekTarget = position;
        _reconciliationLatchExpiry = Environment.TickCount64 + ReconciliationLatchMilliseconds;
        _player.SeekAbsolute(position, exact);
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
        _lastActivityMilliseconds = Environment.TickCount64;
        _chapterOverlay.Layout();
        SetControlsVisible(true);
    }

    private void RegisterPointerActivity(double x, double y)
    {
        if (Math.Abs(x - _lastPointerX) < 0.2 && Math.Abs(y - _lastPointerY) < 0.2) return;
        _lastPointerX = x;
        _lastPointerY = y;
        RegisterActivity();
        UpdateInfoCue(y);
    }

    private void UpdateInfoCue(double y)
    {
        var height = Widget.GetAllocatedHeight();
        var atBottomEdge = _hasMedia && !_infoOpen && height > 0 && y >= height - 28;
        if (_bottomEdgeActive == atBottomEdge) return;
        _bottomEdgeActive = atBottomEdge;
        _infoCueButton.SetVisible(atBottomEdge);
    }

    private bool HasOpenControlPopover()
    {
        return _volumePopover.GetVisible() || _settingsPopover.GetVisible() || _infoOpen;
    }

    private void ToggleVideoInfo()
    {
        if (_infoOpen)
            CloseVideoInfo();
        else
            OpenVideoInfo();
    }

    private void OnInfoCueButtonClicked(object? sender, EventArgs args)
    {
        OpenVideoInfo();
    }

    private void OnInfoCloseButtonClicked(object? sender, EventArgs args)
    {
        CloseVideoInfo();
    }

    private void OnInfoBackdropClicked(object? sender, EventArgs args)
    {
        CloseVideoInfo();
    }

    private void OpenVideoInfo()
    {
        if (!_hasMedia || _currentVideo is not { } video) return;

        _infoOpen = true;
        _bottomEdgeActive = false;
        _infoCueButton.SetVisible(false);
        _infoBackdrop.SetVisible(true);
        _infoRevealer.RevealChild = true;
        _infoTitleLabel.SetText(video.Title);
        _infoChannelLabel.SetText(video.ChannelName);
        _infoCloseButton.GrabFocus();
        _infoStatusLabel.SetText("Loading video details…");
        _infoStatusLabel.SetVisible(true);
        _infoDescriptionScroller.SetVisible(false);
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _infoLoadCancellation = cancellation;
        var generation = ++_infoLoadGeneration;
        LoadVideoInfoAsync(video.Id, generation, cancellation).FireAndForget(Logger);
    }

    private async Task LoadVideoInfoAsync(string videoId, int generation, CancellationTokenSource cancellation)
    {
        YouTubeVideoDetailsResult result;
        try
        {
            result = await _videoDetails.GetDetailsAsync(videoId, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load video details for {VideoId}", videoId);
            result = new YouTubeVideoDetailsResult(null, false, "Video details could not be loaded.");
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || !_infoOpen || generation != _infoLoadGeneration ||
                _currentVideo?.Id != videoId)
                return false;

            if (result is { IsSuccess: true, Details: { } details })
            {
                SetInfoContent(details);
            }
            else
            {
                _infoStatusLabel.SetText(result.StatusMessage);
                _infoStatusLabel.SetVisible(true);
                _infoDescriptionScroller.SetVisible(false);
            }

            return false;
        });
    }

    private void CloseVideoInfo()
    {
        if (!_infoOpen && !_infoRevealer.RevealChild) return;
        _infoOpen = false;
        ++_infoLoadGeneration;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        _infoRevealer.RevealChild = false;
        _infoBackdrop.SetVisible(false);
        _infoCueButton.SetVisible(false);
        SetInfoContent(null);
        if (_hasMedia) _playerSurface.GrabFocus();
    }

    private void SetInfoContent(YouTubeVideoDetails? details)
    {
        if (details is null)
        {
            _infoTitleLabel.SetText(_currentVideo?.Title ?? "Video details");
            _infoChannelLabel.SetText(_currentVideo?.ChannelName ?? string.Empty);
            _infoStatsLabel.SetText(string.Empty);
            _infoStatusLabel.SetText("Move to the bottom edge to reveal video details.");
            _infoStatusLabel.SetVisible(true);
            _infoDescriptionScroller.SetVisible(false);
            return;
        }

        _infoTitleLabel.SetText(details.Title);
        _infoChannelLabel.SetText(details.ChannelName);
        _infoStatsLabel.SetText(BuildInfoStats(details));
        _infoStatusLabel.SetVisible(string.IsNullOrWhiteSpace(details.Description));
        _infoStatusLabel.SetText("This video has no description.");
        _infoDescriptionScroller.SetVisible(!string.IsNullOrWhiteSpace(details.Description));
        if (_infoDescription.Buffer is { } buffer)
            buffer.Text = details.Description ?? string.Empty;
    }

    private static string BuildInfoStats(YouTubeVideoDetails details)
    {
        var parts = new List<string>();
        if (details.ViewCount is { } viewCount and >= 0)
            parts.Add($"{viewCount.ToString("N0", CultureInfo.CurrentCulture)} views");
        if (details.PublishedAt is { } publishedAt)
            parts.Add($"Published {publishedAt.ToLocalTime():d}");
        return string.Join(" · ", parts);
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

    private void OnQueueButtonToggled(object? sender, EventArgs args)
    {
    }

    private void CloseQueue()
    {
        _queueButton.Active = false;
    }

    private void OnTrackJumpRequested(int index)
    {
        if (_request is null || index < 0 || index >= _request.Videos.Length)
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

    private void OnInfoChannelButtonClicked(object? sender, EventArgs args)
    {
        CloseVideoInfo();
        OpenCurrentChannel();
    }

    private void OpenCurrentChannel()
    {
        if (_currentVideo is { } video)
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


    private void OnTimelineMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        if (_disposed || !_hasMedia || _currentDuration <= TimeSpan.Zero || !_timeline.GetSensitive())
        {
            _scrubCue.SetVisible(false);
            return;
        }

        RegisterActivity();
        UpdateScrubCue(args.X);
    }

    private void OnTimelineLeave(object? sender, EventArgs args)
    {
        if (!_isScrubbing)
            _scrubCue.SetVisible(false);
    }

    private void UpdateScrubCue(double pointerX)
    {
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(_timeline, _timelineOverlay,
            _isScrubbing ? TimeSpan.FromSeconds(_timeline.GetValue()) : _timelinePlaybackPosition,
            _currentDuration);

        var targetTime =
            PlayerTimelineGeometry.GetPositionAtCoordinate(pointerX, trackStart, trackWidth, _currentDuration);

        var cueWidth = _scrubCue.GetAllocatedWidth();
        var hostWidth = _timelineOverlay.GetAllocatedWidth();
        if (cueWidth <= 0) cueWidth = 80;
        var badgeX = Math.Clamp(pointerX - cueWidth / 2d, 8, Math.Max(8, hostWidth - cueWidth - 8));
        _scrubCue.MarginStart = (int)Math.Round(badgeX);

        _scrubTimeLabel.SetText(FormatTime(targetTime));

        if (_isScrubbing)
        {
            var delta = targetTime - _scrubStartPosition;
            _scrubDeltaLabel.SetText(FormatDelta(delta));
            _scrubDeltaLabel.SetVisible(true);
        }
        else
        {
            _scrubDeltaLabel.SetVisible(false);
        }

        var chapter = FindChapterAt(targetTime);
        if (chapter is not null && !string.IsNullOrWhiteSpace(chapter.Title))
        {
            _scrubChapterLabel.SetText(chapter.Title);
            _scrubChapterLabel.SetVisible(true);
        }
        else
        {
            _scrubChapterLabel.SetVisible(false);
        }

        _scrubCue.SetVisible(true);
    }

    private void OnTimelineDragBegin(GestureDrag sender, GestureDrag.DragBeginSignalArgs args)
    {
        if (_disposed || !_hasMedia || !_timeline.GetSensitive() || _currentDuration <= TimeSpan.Zero)
            return;

        _isScrubbing = true;
        _scrubStartPosition = _timelinePlaybackPosition;
        _latestScrubPosition = _timeline.GetValue();
        _positionLabel.AddCssClass("player-time-scrubbing");
        _timeline.AddCssClass("dragging");
        RegisterActivity();
        UpdateScrubCue(args.StartX);
    }

    private void OnTimelineDragUpdate(GestureDrag sender, GestureDrag.DragUpdateSignalArgs args)
    {
        if (!_isScrubbing) return;
        RegisterActivity();
        sender.GetStartPoint(out var startX, out _);
        UpdateScrubCue(startX + args.OffsetX);
    }

    private void OnTimelineDragEnd(GestureDrag sender, GestureDrag.DragEndSignalArgs args)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        CancelThrottledSeek();

        var finalPosition = _timeline.GetValue();
        SeekAbsolute(finalPosition);
        _timelinePlaybackPosition = TimeSpan.FromSeconds(finalPosition);
        _positionLabel.SetText(FormatTime(_timelinePlaybackPosition));
        RegisterActivity();
    }

    private void CancelScrubbing()
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        CancelThrottledSeek();
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetValue(_scrubStartPosition.TotalSeconds);
            _timelinePlaybackPosition = _scrubStartPosition;
            _positionLabel.SetText(FormatTime(_scrubStartPosition));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void CancelThrottledSeek()
    {
        if (_throttledSeekSource == 0) return;
        Functions.SourceRemove(_throttledSeekSource);
        _throttledSeekSource = 0;
    }

    private static string FormatDelta(TimeSpan delta)
    {
        var sign = delta < TimeSpan.Zero ? "-" : "+";
        var abs = delta.Duration();
        return abs.TotalHours >= 1
            ? $"{sign}{(int)abs.TotalHours}:{abs.Minutes:D2}:{abs.Seconds:D2}"
            : $"{sign}{(int)abs.TotalMinutes}:{abs.Seconds:D2}";
    }

    private LibMpvChapter? FindChapterAt(TimeSpan position)
    {
        LibMpvChapter? match = null;
        foreach (var chapter in _chapters)
            if (chapter.Start <= position)
                match = chapter;
            else
                break;

        return match;
    }

    private void OnTimelineValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls || !_timeline.GetSensitive()) return;

        var targetSeconds = _timeline.GetValue();
        _timelinePlaybackPosition = TimeSpan.FromSeconds(targetSeconds);
        _positionLabel.SetText(FormatTime(_timelinePlaybackPosition));

        if (_isScrubbing)
        {
            _latestScrubPosition = targetSeconds;
            var now = Environment.TickCount64;
            var elapsed = now - _lastThrottledSeekTime;
            if (elapsed >= SeekThrottleIntervalMilliseconds && _throttledSeekSource == 0)
            {
                _lastThrottledSeekTime = now;
                SeekAbsolute(_latestScrubPosition, false);
            }
            else if (_throttledSeekSource == 0)
            {
                var delay = Math.Max(10u, (uint)(SeekThrottleIntervalMilliseconds - elapsed));
                _throttledSeekSource = Functions.TimeoutAdd(0, delay, () =>
                {
                    _throttledSeekSource = 0;
                    if (_disposed || !_isScrubbing) return false;
                    _lastThrottledSeekTime = Environment.TickCount64;
                    SeekAbsolute(_latestScrubPosition, false);
                    return false;
                });
            }
        }
        else
        {
            SeekAbsolute(targetSeconds);
            RegisterActivity();
        }
    }

    private void OnRenderRequested(object? sender, EventArgs args)
    {
        if (_disposed) return;

        _playerSurface.QueueRender();
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
            _watchProgress.Update(playbackRequest, playbackState);
        }

        _desktopMedia.UpdatePlayback(_request, state);
        SetLoading(state.IsLoading);
        _updatingControls = true;
        _subtitleController.UpdateTracks(state.SubtitleTracks, _updatingControls);
        try
        {
            _durationLabel.SetText(state.Duration == TimeSpan.Zero ? "Live" : FormatTime(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);
            _chapterOverlay.Update(state.Chapters, state.Duration);

            if (!_isScrubbing)
            {
                var withinLatch = Environment.TickCount64 < _reconciliationLatchExpiry;
                var isCloseToPending = Math.Abs(state.Position.TotalSeconds - _pendingSeekTarget) <= 1.5;

                if (!withinLatch || isCloseToPending)
                {
                    if (isCloseToPending) _reconciliationLatchExpiry = 0;
                    _timelinePlaybackPosition = state.Position;
                    _positionLabel.SetText(FormatTime(state.Position));
                    _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0,
                        Math.Max(0, state.Duration.TotalSeconds)));
                }
            }

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
            _currentPlaylistIndex = state.PlaylistIndex;
            _queueViewModel.SetCurrentPlayingIndex(state.PlaylistIndex);
            _previousQueueButton.Sensitive = state.PlaylistIndex > 0;
            _nextQueueButton.Sensitive = state.PlaylistIndex < request.Videos.Length - 1;
            var video = request.Videos[state.PlaylistIndex];
            if (_currentVideo?.Id != video.Id && _infoOpen)
                CloseVideoInfo();
            _currentVideo = video;
            _titleLabel.SetText(video.Title);
            _channelLabel.SetText(video.ChannelName);
            if (!string.Equals(_commentsVideoId, video.Id, StringComparison.Ordinal))
            {
                foreach (var feature in _features) feature.Load(video);
            }

            foreach (var feature in _features) feature.UpdatePlayback(state, video.Id);


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
        CloseVideoInfo();
        _titleLabel.SetText("Playback failed");
        SetLoading(false);
        _channelLabel.SetText($"Embedded playback failed: {detail}");
        CancelScrubbing();
        _currentDuration = TimeSpan.Zero;
        _chapters = [];
        _scrubCue.SetVisible(false);
        ResetTransport();
        foreach (var feature in _features) feature.Clear();

        _chapterOverlay.Update([], TimeSpan.Zero);
        _commentsView.SetVideo(null);
        _commentsVideoId = null;
        _request = null;
        _currentVideo = null;
        _queueControls.SetVisible(false);
        _queueButton.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _currentPlaylistIndex = -1;
        _hasMedia = false;
        _player.Stop();
        ReleaseSession();
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        CancelScrubbing();
        _currentDuration = TimeSpan.Zero;
        _chapters = [];
        _scrubCue.SetVisible(false);
        CloseVideoInfo();
        ReleaseSession();
        _request = null;
        _currentVideo = null;
        _hasMedia = false;
        _queueControls.SetVisible(false);
        _queueButton.Active = false;
        _queueViewModel.SetCurrentPlayingIndex(-1);
        _currentPlaylistIndex = -1;
        foreach (var feature in _features) feature.Clear();

        _chapterOverlay.Update([], TimeSpan.Zero);
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
        CancelScrubbing();
        _currentDuration = TimeSpan.Zero;
        _chapters = [];
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetRange(0, 0);
            _timeline.SetValue(0);
            _timeline.SetSensitive(false);
            _playPauseButton.SetIconName("media-playback-start-symbolic");
            _positionLabel.SetText("0:00");
            _timelinePlaybackPosition = TimeSpan.Zero;
        }
        finally
        {
            _updatingControls = false;
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
            if (_disposed || _syncingQueue || _request is null)
                return false;

            var currentVideos = _request.Videos;
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

            _request = new PlaybackRequest(newVideos);
            _queueControls.SetVisible(_request.Videos.Length > 1);
            _previousQueueButton.Sensitive = _currentPlaylistIndex > 0;
            _nextQueueButton.Sensitive = _currentPlaylistIndex < _request.Videos.Length - 1;
            return false;
        });
    }


    private static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (long)Math.Floor(value.TotalSeconds));
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    private enum PlayerShortcutAction
    {
        TogglePause,
        SeekBackward,
        SeekForward,
        StepFrameBackward,
        StepFrameForward,
        ToggleMute,
        VolumeUp,
        VolumeDown,
        SeekToBeginning,
        ReturnToShell,
        ToggleVideoInfo,
        SpeedDecrease,
        SpeedIncrease,
        NextVideo,
        PreviousVideo,
        ToggleFullscreen,
        PreferredSubtitle,
        ResumeOrSkip,
        ToggleQueue
    }

    private sealed class EmbeddedPlayerPlaybackService(EmbeddedPlayerView player) : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request)
        {
            return player.PresentAsync(request);
        }
    }
}