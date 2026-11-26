using System.Globalization;
using Gtk;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controls;

/// <summary>
///     Encapsulates the bottom controls bar, including video metadata, queue navigation,
///     embedded reactions and timeline widgets, volume/settings popovers, and subtitles.
/// </summary>
public partial class PlayerControlsBarView : ViewBase<Box>
{
    private static readonly string[] DefaultQualities = ["Best", "1080p", "720p", "480p", "360p", "240p", "144p"];
    private static readonly ILogger Logger = Log.ForContext<PlayerControlsBarView>();
    private readonly List<string> _availableQualities = ["Best"];
    private readonly INavigationService _navigation;
    private string? _channelUrl;
    private string? _currentQualityVideoId;
    private (bool Visible, bool CanPrevious, bool CanNext, bool IsQueueOpen)? _lastQueueControls;
    private double _lastSpeed = double.NaN;
    private double _lastVolume = double.NaN;
    private string? _lastVolumeIcon;
    private CancellationTokenSource? _qualityCancellation;
    private int _qualityGeneration;
    private PlayerSubtitleController? _subtitleController;
    private bool _suppressQualityNotification;
    private bool _suppressSpeedNotification;
    private bool _suppressVolumeNotification;

    public PlayerControlsBarView(
        INavigationService navigation,
        PlayerTimelineView timeline,
        PlayerReactionsView reactions)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        Reactions = reactions ?? throw new ArgumentNullException(nameof(reactions));
        player_reactions_host.Append(Reactions.Widget);
        player_timeline_host.Append(Timeline.Widget);
    }

    private PlayerTimelineView Timeline { get; }
    private PlayerReactionsView Reactions { get; }
    public bool IsPopoverOpen => player_volume_popover.GetVisible() || player_settings_popover.GetVisible();

    public bool IsQueueActive => player_queue_button.Active;

    public event EventHandler? PreviousQueueClicked;
    public event EventHandler? NextQueueClicked;
    public event EventHandler<bool>? QueueToggled;
    public event EventHandler<double>? VolumeChanged;
    public event EventHandler<double>? SpeedChanged;
    public event EventHandler<string>? QualitySelected;
    public event EventHandler<string>? SubtitleToggled;

    public void AttachSubtitleController(IPreferencesService preferences, Action<long> selectTrack)
    {
        _subtitleController = Lifetime.Own(new PlayerSubtitleController(
            preferences, player_subtitle_dropdown, player_subtitle_model, player_subtitle_button, selectTrack));
    }

    public void UpdateSubtitleTracks(IReadOnlyList<LibMpvSubtitleTrack> tracks, bool suppress = false)
    {
        _subtitleController?.UpdateTracks(tracks, suppress);
    }

    public string ShowPreferredSubtitle()
    {
        return _subtitleController?.ShowPreferredSubtitle() ?? string.Empty;
    }

    public void ToggleQueue()
    {
        player_queue_button.Active = !player_queue_button.Active;
    }

    public void SetQueueActive(bool active)
    {
        player_queue_button.Active = active;
    }

    public void SetMetadata(string title, string channelName, string? channelUrl)
    {
        _channelUrl = channelUrl;
        player_title_label.SetText(string.IsNullOrWhiteSpace(title) ? "Nothing playing" : title);
        player_channel_label.SetText(string.IsNullOrWhiteSpace(channelName)
            ? "Choose a video to preview the embedded player."
            : channelName);
        player_channel_button.Sensitive = !string.IsNullOrWhiteSpace(channelUrl);
    }

    public void SetQueueControls(bool visible, bool canPrevious, bool canNext, bool isQueueOpen)
    {
        var next = (visible, canPrevious, canNext, isQueueOpen);
        if (_lastQueueControls == next && player_queue_button.Active == isQueueOpen) return;
        _lastQueueControls = next;
        player_queue_controls.SetVisible(visible);
        player_previous_queue_button.Sensitive = canPrevious;
        player_next_queue_button.Sensitive = canNext;
        if (player_queue_button.Active != isQueueOpen)
            player_queue_button.Active = isQueueOpen;
    }

    public void SetVolume(double volume)
    {
        if (_lastVolume.Equals(volume)) return;
        _lastVolume = volume;
        _suppressVolumeNotification = true;
        player_volume_scale.SetValue(volume);
        _suppressVolumeNotification = false;
        UpdateVolumeIcon(volume);
    }

    public void SetSpeed(double speed)
    {
        if (_lastSpeed.Equals(speed)) return;
        _lastSpeed = speed;
        _suppressSpeedNotification = true;
        player_speed_scale.SetValue(speed);
        player_speed_label.SetText($"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×");
        _suppressSpeedNotification = false;
    }

    public void LoadQualityTracks(string videoId, IYouTubeMediaResolver resolver, string targetQuality)
    {
        if (string.Equals(_currentQualityVideoId, videoId, StringComparison.Ordinal)) return;
        _currentQualityVideoId = videoId;
        _qualityCancellation?.Cancel();
        _qualityCancellation?.Dispose();
        _qualityCancellation = null;

        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
        {
            ApplyQualityTracks([], targetQuality);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _qualityCancellation = cancellation;
        var generation = ++_qualityGeneration;
        resolver.GetQualityTracksAsync(videoId, cancellation.Token).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && !cancellation.IsCancellationRequested &&
                generation == _qualityGeneration)
                GtkUiDispatch.Run(
                    () => !IsDisposed && !cancellation.IsCancellationRequested && generation == _qualityGeneration,
                    () => ApplyQualityTracks(task.Result, targetQuality));
        }, TaskScheduler.Default);
    }

    private void ApplyQualityTracks(IReadOnlyList<string> tracks, string targetQuality)
    {
        _suppressQualityNotification = true;
        _availableQualities.Clear();
        _availableQualities.Add("Best");
        var list = tracks.Count > 0 ? tracks : DefaultQualities.Skip(1);
        foreach (var q in list)
            if (!_availableQualities.Contains(q, StringComparer.OrdinalIgnoreCase))
                _availableQualities.Add(q);

        player_quality_model.Splice(0, player_quality_model.GetNItems(), [.. _availableQualities]);
        var index = _availableQualities.FindIndex(q =>
            string.Equals(q, targetQuality, StringComparison.OrdinalIgnoreCase));
        player_quality_dropdown.SetSelected((uint)Math.Max(0, index));
        _suppressQualityNotification = false;
    }

    private void UpdateVolumeIcon(double volume)
    {
        var icon = volume switch
        {
            <= 0 => "audio-volume-muted-symbolic",
            < 33 => "audio-volume-low-symbolic",
            < 66 => "audio-volume-medium-symbolic",
            _ => "audio-volume-high-symbolic"
        };
        if (string.Equals(_lastVolumeIcon, icon, StringComparison.Ordinal)) return;
        _lastVolumeIcon = icon;
        player_volume_button.SetIconName(icon);
    }

    private void OnChannelButtonClicked(object? sender, EventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(_channelUrl))
            _navigation.NavigateToChannel(_channelUrl, player_channel_label.GetText());
    }

    private void OnPreviousQueueButtonClicked(object? sender, EventArgs args)
    {
        PreviousQueueClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnQueueButtonToggled(object? sender, EventArgs args)
    {
        var active = player_queue_button.Active;
        if (_lastQueueControls is { } prev)
            _lastQueueControls = prev with { IsQueueOpen = active };
        QueueToggled?.Invoke(this, active);
    }

    private void OnNextQueueButtonClicked(object? sender, EventArgs args)
    {
        NextQueueClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnSubtitleButtonClicked(object? sender, EventArgs args)
    {
        var subtitle = ShowPreferredSubtitle();
        SubtitleToggled?.Invoke(this, subtitle);
    }

    private void OnSubtitleDropdownNotify(object? sender, EventArgs args)
    {
        _subtitleController?.OnSelectionChanged();
    }

    private void OnVolumeScaleValueChanged(object? sender, EventArgs args)
    {
        var volume = player_volume_scale.GetValue();
        _lastVolume = volume;
        UpdateVolumeIcon(volume);
        if (!_suppressVolumeNotification) VolumeChanged?.Invoke(this, volume);
    }

    private void OnSpeedScaleValueChanged(object? sender, EventArgs args)
    {
        var speed = player_speed_scale.GetValue();
        _lastSpeed = speed;
        player_speed_label.SetText($"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×");
        if (!_suppressSpeedNotification) SpeedChanged?.Invoke(this, speed);
    }

    private void OnQualityDropdownNotify(object? sender, EventArgs args)
    {
        if (_suppressQualityNotification) return;
        var selected = player_quality_dropdown.GetSelected();
        var item = player_quality_model.GetString(selected);
        if (!string.IsNullOrEmpty(item)) QualitySelected?.Invoke(this, item);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _qualityCancellation?.Cancel();
            _qualityCancellation?.Dispose();
            _qualityCancellation = null;
        }

        base.Dispose(disposing);
    }
}