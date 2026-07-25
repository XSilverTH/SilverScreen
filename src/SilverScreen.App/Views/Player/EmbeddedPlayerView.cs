using static Gdk.Constants;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.Infrastructure.Features.Session;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Player;

internal interface IEmbeddedPlayerPresenter
{
    Task<string> PresentAsync(PlaybackRequest request);
}

public partial class EmbeddedPlayerView : ViewBase<Overlay>, IEmbeddedPlayerPresenter, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<EmbeddedPlayerView>();
    private static readonly double[] Speeds = [0.5, 0.75, 1, 1.25, 1.5, 2];
    private const long ControlsIdleDelayMilliseconds = 1_500;
    private const uint ControlsVisibilityCheckMilliseconds = 100;
    private readonly Action _backRequested;
    private readonly Box _centerControls;
    private readonly Label _channelLabel;
    private readonly ICookieFileProvider _cookieFiles;
    private readonly Label _durationLabel;
    private readonly Box _loadingIndicator;
    private readonly Label _dislikesLabel;
    private readonly Button _dislikeButton;
    private readonly IPlaybackPresenceService _playbackPresence;
    private readonly LibMpvPlayer _player;
    private readonly ISessionService _session;
    private readonly IVideoEngagementService _videoEngagement;
    private readonly Widget _headerBar;
    private readonly GLArea _playerSurface;
    private readonly Button _playPauseButton;
    private readonly Label _positionLabel;
    private readonly Button _likeButton;
    private readonly Label _likesLabel;
    private readonly IPreferencesService _preferences;
    private readonly Widget _playerControls;
    private readonly Action _presentRequested;
    private readonly DropDown _qualityDropdown;
    private readonly DropDown _speedDropdown;
    private readonly Scale _timeline;
    private readonly Label _titleLabel;
    private readonly MenuButton _volumeButton;
    private readonly IYouTubeRatingService _youtubeRating;
    private readonly Scale _volumeScale;
    private CancellationTokenSource? _engagementCancellation;
    private long _engagementLoadVersion;
    private string? _engagementVideoId;
    private YouTubeRatingState _ratingState;
    private CookieFileLease? _cookieFile;
    private bool _disposed;
    private bool _hasMedia;
    private bool _rendererReady;
    private PlaybackRequest? _request;
    private bool _updatingControls;
    private bool _controlsVisible = true;
    private double _speed = 1;
    private long _lastActivityMilliseconds;
    private double _lastPointerX = double.NaN;
    private double _lastPointerY = double.NaN;
    private uint _controlsAutohideSource;
    private readonly Dictionary<uint, Action> _shortcutMap = [];

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, IPreferencesService preferences,
        ICookieFileProvider cookieFiles, IPlaybackPresenceService playbackPresence,
        IVideoEngagementService videoEngagement,
        IYouTubeRatingService youtubeRating, ISessionService session)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _preferences = preferences;
        _cookieFiles = cookieFiles;
        _playbackPresence = playbackPresence;
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
        _qualityDropdown = GetRequiredObject<DropDown>("player_quality_dropdown");
        _speedDropdown = GetRequiredObject<DropDown>("player_speed_dropdown");
        _timeline = GetRequiredObject<Scale>("player_timeline");
        _loadingIndicator = GetRequiredObject<Box>("player_loading_indicator");
        _titleLabel = GetRequiredObject<Label>("player_title_label");
        _channelLabel = GetRequiredObject<Label>("player_channel_label");
        _positionLabel = GetRequiredObject<Label>("player_position_label");
        _durationLabel = GetRequiredObject<Label>("player_duration_label");
        _likesLabel = GetRequiredObject<Label>("player_likes_label");
        _likeButton = GetRequiredObject<Button>("player_like_button");
        _dislikeButton = GetRequiredObject<Button>("player_dislike_button");
        _dislikesLabel = GetRequiredObject<Label>("player_dislikes_label");
        SetReactionSensitive(false);
        _player = new LibMpvPlayer(action => Functions.IdleAdd(0, () =>
        {
            if (!_disposed) action();
            return false;
        }));
        _player.RenderRequested += OnRenderRequested;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackFailed += OnPlaybackFailed;
        _player.PlaybackEnded += OnPlaybackEnded;
        SetControls(100, 1, "Best");
        SetupControlsAutohide();
        SetupKeyboardShortcuts();

        DeclareBindings();
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

        void Bind(Action action, params uint[] keys)
        {
            foreach (var key in keys)
                _shortcutMap[key] = action;
        }
    }

    public new void Dispose()
    {
        if (_disposed) return;
        if (_controlsAutohideSource != 0) Functions.SourceRemove(_controlsAutohideSource);
        CancelEngagementLoad();
        _disposed = true;
        if (_rendererReady)
        {
            _playerSurface.MakeCurrent();
            _player.ShutdownRenderer();
            _rendererReady = false;
        }

        _player.RenderRequested -= OnRenderRequested;
        _player.StateChanged -= OnStateChanged;
        _player.PlaybackFailed -= OnPlaybackFailed;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.Dispose();
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
            _cookieFile = _cookieFiles.CreateCookieFile();
            var preferences = _preferences.GetPreferences();
            var firstVideo = request.Videos[0];
            _titleLabel.SetText(firstVideo.Title);
            _channelLabel.SetText(firstVideo.ChannelName);
            _durationLabel.SetText(FormatTime(firstVideo.Duration));
            LoadEngagement(firstVideo);
            RegisterActivity();
            SetControls(100, 1, NormalizeQuality(preferences.VideoQuality));
            SetLoading(true);
            _hasMedia = false;
            _presentRequested();
            Widget.GrabFocus();
            if (_rendererReady) _player.Load(request, preferences, _cookieFile?.Path);
            return false;
        });

        return Task.FromResult("Opening embedded player.");
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
        Widget.AddController(key);
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
        var speedIndex = Array.IndexOf(Speeds, _speed);
        _player.SetSpeed(Speeds[Math.Clamp((speedIndex < 0 ? 2 : speedIndex) + direction, 0, Speeds.Length - 1)]);
    }

    private void ToggleFullscreen()
    {
        if (Widget.GetRoot() is Window window) window.Fullscreened = !window.Fullscreened;
    }

    private void RegisterActivity()
    {
        _lastActivityMilliseconds = Environment.TickCount64;
        SetControlsVisible(true);
    }
    private void RegisterPointerActivity(double x, double y)
    {
        if (Math.Abs(x - _lastPointerX) < 0.2 && Math.Abs(y - _lastPointerY) < 0.2) return;
        _lastPointerX = x;
        _lastPointerY = y;
        RegisterActivity();
    }


    private void SetControlsVisible(bool visible)
    {
        if (_controlsVisible == visible) return;
        _controlsVisible = visible;
        SetControlVisible(_headerBar, visible);
        SetControlVisible(_centerControls, visible);
        SetControlVisible(_playerControls, visible);
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

    private void OnVolumeScaleValueChanged(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetVolume(_volumeScale.GetValue());
    }

    private void OnQualityDropdownNotify(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetQuality(QualityAt(_qualityDropdown.GetSelected()));
    }

    private void OnSpeedDropdownNotify(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetSpeed(SpeedAt(_speedDropdown.GetSelected()));
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
            _playbackPresence.SetPlaybackState(playbackRequest, new PlaybackPresenceState(state.PlaylistIndex, state.Position,
                state.Duration, state.IsPaused, state.Speed, DateTimeOffset.UtcNow));
        }
        SetLoading(state.IsLoading);
        _updatingControls = true;
        try
        {
            _positionLabel.SetText(FormatTime(state.Position));
            _durationLabel.SetText(state.Duration == TimeSpan.Zero ? "Live" : FormatTime(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0, Math.Max(0, state.Duration.TotalSeconds)));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);
            _playPauseButton.SetIconName(state.HasMedia && !state.IsPaused
                ? "media-playback-pause-symbolic"
                : "media-playback-start-symbolic");
            _playPauseButton.SetTooltipText(state.HasMedia && !state.IsPaused
                ? "Pause (Space or K)"
                : "Play (Space or K)");
            _volumeScale.SetValue(Math.Clamp(state.Volume, 0, 100));
            _volumeButton.SetIconName(VolumeIcon(state.Volume, state.IsMuted));
            var speedIndex = Array.IndexOf(Speeds, state.Speed);
            _speedDropdown.SetSelected((uint)(speedIndex < 0 ? 2 : speedIndex));
            if (_request is { } request && state.PlaylistIndex is >= 0 and < int.MaxValue &&
                state.PlaylistIndex < request.Videos.Length)
            {
                var video = request.Videos[state.PlaylistIndex];
                _titleLabel.SetText(video.Title);
                _channelLabel.SetText(video.ChannelName);
                if (!string.Equals(_engagementVideoId, video.Id, StringComparison.Ordinal))
                    LoadEngagement(video);
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void OnPlaybackEnded(object? sender, EventArgs args)
    {
        _hasMedia = false;
        ReleaseSession();
    }

    private void OnPlaybackFailed(object? sender, string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _titleLabel.SetText("Playback failed");
        SetLoading(false);
        _channelLabel.SetText($"Embedded playback failed: {detail}");
        ResetTransport();
        CancelEngagementLoad();
        _request = null;
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
        CancelEngagementLoad();
        SetLoading(false);
    }

    private void ReleaseSession()
    {
        _playbackPresence.Clear();
        _cookieFile?.Dispose();
        _cookieFile = null;
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
            _speedDropdown.SetSelected((uint)Math.Max(0, Array.IndexOf(Speeds, speed)));
            _qualityDropdown.SetSelected(
                (uint)Array.IndexOf(new[] { "Best", "1080p", "720p", "480p", "360p" }, quality));
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

    private static double SpeedAt(uint selected)
    {
        return Speeds[Math.Min(selected, (uint)(Speeds.Length - 1))];
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
        if (ratingState == YouTubeRatingState.Like)
            _likeButton.AddCssClass("player-reaction-selected");
        else
            _likeButton.RemoveCssClass("player-reaction-selected");

        if (ratingState == YouTubeRatingState.Dislike)
            _dislikeButton.AddCssClass("player-reaction-selected");
        else
            _dislikeButton.RemoveCssClass("player-reaction-selected");
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