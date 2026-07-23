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
    private readonly Action _backRequested;
    private readonly Box _centerControls;
    private readonly Label _channelLabel;
    private readonly ICookieFileProvider _cookieFiles;
    private readonly Label _durationLabel;
    private readonly Box _loadingIndicator;
    private readonly IPlaybackPresenceService _playbackPresence;
    private readonly LibMpvPlayer _player;
    private readonly GLArea _playerSurface;
    private readonly ToggleButton _playPauseButton;
    private readonly Label _positionLabel;
    private readonly IPreferencesService _preferences;
    private readonly Action _presentRequested;
    private readonly DropDown _qualityDropdown;
    private readonly DropDown _speedDropdown;
    private readonly Scale _timeline;
    private readonly Label _titleLabel;
    private readonly MenuButton _volumeButton;
    private readonly Scale _volumeScale;
    private CookieFileLease? _cookieFile;
    private bool _disposed;
    private bool _rendererReady;
    private PlaybackRequest? _request;
    private bool _updatingControls;

    public EmbeddedPlayerView(Action presentRequested, Action backRequested, IPreferencesService preferences,
        ICookieFileProvider cookieFiles, IPlaybackPresenceService playbackPresence)
    {
        _presentRequested = presentRequested;
        _backRequested = backRequested;
        _preferences = preferences;
        _cookieFiles = cookieFiles;
        _playbackPresence = playbackPresence;
        _playerSurface = GetRequiredObject<GLArea>("player_surface");
        _centerControls = GetRequiredObject<Box>("player_center_controls");
        _playPauseButton = GetRequiredObject<ToggleButton>("player_play_pause_button");
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
    }

    public new void Dispose()
    {
        if (_disposed) return;
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
            SetControls(100, 1, NormalizeQuality(preferences.VideoQuality));
            _playbackPresence.SetPlaying(request, DateTimeOffset.UtcNow);
            SetLoading(true);
            _presentRequested();
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

    private void OnBackButtonClicked(object? sender, EventArgs args)
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

    private void OnPlayPauseButtonToggled(object? sender, EventArgs args)
    {
        if (!_updatingControls) _player.SetPaused(!_playPauseButton.GetActive());
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
        SetLoading(state.IsLoading);
        _updatingControls = true;
        try
        {
            _positionLabel.SetText(FormatTime(state.Position));
            _durationLabel.SetText(state.Duration == TimeSpan.Zero ? "Live" : FormatTime(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0, Math.Max(0, state.Duration.TotalSeconds)));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);
            _playPauseButton.SetActive(state.HasMedia && !state.IsPaused);
            _playPauseButton.SetIconName(state.HasMedia && !state.IsPaused
                ? "media-playback-pause-symbolic"
                : "media-playback-start-symbolic");
            _playPauseButton.SetTooltipText(state.HasMedia && !state.IsPaused ? "Pause" : "Play");
            _volumeScale.SetValue(Math.Clamp(state.Volume, 0, 100));
            _volumeButton.SetIconName(VolumeIcon(state.Volume));
            var speedIndex = Array.IndexOf(Speeds, state.Speed);
            _speedDropdown.SetSelected((uint)(speedIndex < 0 ? 2 : speedIndex));
            if (_request is { } request && state.PlaylistIndex is >= 0 and < int.MaxValue &&
                state.PlaylistIndex < request.Videos.Length)
            {
                var video = request.Videos[state.PlaylistIndex];
                _titleLabel.SetText(video.Title);
                _channelLabel.SetText(video.ChannelName);
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void OnPlaybackEnded(object? sender, EventArgs args)
    {
        ReleaseSession();
    }

    private void OnPlaybackFailed(object? sender, string detail)
    {
        Logger.Error("Embedded playback failed: {Detail}", detail);
        _titleLabel.SetText("Playback failed");
        SetLoading(false);
        _channelLabel.SetText($"Embedded playback failed: {detail}");
        ResetTransport();
        _request = null;
        _player.Stop();
        ReleaseSession();
    }

    private void EndSession(bool stop)
    {
        if (stop) _player.Stop();
        ReleaseSession();
        _request = null;
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
            _playPauseButton.SetActive(false);
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
            _volumeButton.SetIconName(VolumeIcon(volume));
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

    private static string VolumeIcon(double volume)
    {
        return volume <= 0 ? "audio-volume-muted-symbolic" :
            volume <= 50 ? "audio-volume-low-symbolic" : "audio-volume-high-symbolic";
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