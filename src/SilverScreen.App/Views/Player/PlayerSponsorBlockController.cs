using Cairo;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;
using static GLib.Functions;

namespace SilverScreen.Views.Player;

internal sealed class PlayerSponsorBlockController : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlayerSponsorBlockController>();
    private const uint SkipPromptDurationMilliseconds = 3_000;
    private readonly HashSet<string> _autoSkippedSegmentIds = new(StringComparer.Ordinal);
    private readonly IPreferencesService _preferences;
    private readonly Action<double> _seekAbsolute;
    private readonly Button _skipButton;
    private readonly ISponsorBlockService _sponsorBlock;
    private readonly Scale _timeline;
    private readonly DrawingArea _timelineDrawingArea;
    private readonly Overlay _timelineOverlay;
    private SponsorBlockSegment? _activeManualSegment;
    private CancellationTokenSource? _cancellation;
    private string _configurationKey = string.Empty;
    private bool _disposed;
    private TimeSpan _duration;
    private LibMpvPlaybackState? _lastPlaybackState;
    private string? _lastPlaybackVideoId;
    private long _loadVersion;
    private bool _manualPromptAfterSeek;
    private bool _manualWasPaused;
    private uint _promptHideSource;
    private IReadOnlyList<SponsorBlockSegment> _segments = [];
    private string? _skipButtonColorClass;
    private VideoSummary? _video;
    private string? _videoId;

    public PlayerSponsorBlockController(ISponsorBlockService sponsorBlock, IPreferencesService preferences,
        Scale timeline, Overlay timelineOverlay, Button skipButton, Action<double> seekAbsolute)
    {
        _sponsorBlock = sponsorBlock;
        _preferences = preferences;
        _timeline = timeline;
        _timelineOverlay = timelineOverlay;
        _skipButton = skipButton;
        _seekAbsolute = seekAbsolute;
        _timelineDrawingArea = DrawingArea.New();
        _timelineDrawingArea.SetCanTarget(false);
        _timelineDrawingArea.Halign = Align.Fill;
        _timelineDrawingArea.Valign = Align.Fill;
        _timelineDrawingArea.SetDrawFunc(DrawTimeline);
        _timelineOverlay.AddOverlay(_timelineDrawingArea);
        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoad();
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        ClearState();
        _timelineOverlay.RemoveOverlay(_timelineDrawingArea);
        _timelineDrawingArea.Dispose();
    }

    public void Load(VideoSummary video)
    {
        if (_disposed) return;
        CancelLoad();
        ClearState();
        _video = video;
        _videoId = video.Id;
        var preferences = _preferences.GetPreferences();
        _configurationKey = GetSponsorBlockConfigurationKey(preferences);
        if (!(preferences.SponsorBlockAutoSkipEnabled || preferences.SponsorBlockSegmentDisplayEnabled) ||
            !PlaybackRequest.LooksLikeYouTubeVideoId(video.Id))
            return;

        var categories = preferences.SponsorBlockCategories.Where(SponsorBlockCategories.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (categories.Length == 0) return;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var loadVersion = ++_loadVersion;
        LoadAsync(video.Id, categories, loadVersion, cancellation.Token).FireAndForget(Logger);
    }

    public void UpdatePlayback(LibMpvPlaybackState state, string videoId)
    {
        if (_disposed) return;
        if (_lastPlaybackState is { } previous &&
            string.Equals(_lastPlaybackVideoId, videoId, StringComparison.Ordinal) &&
            Math.Abs((state.Position - previous.Position).TotalSeconds) > 1)
            _manualPromptAfterSeek = true;
        _lastPlaybackState = state;
        _lastPlaybackVideoId = videoId;
        if (!string.Equals(_videoId, videoId, StringComparison.Ordinal)) return;
        _duration = state.Duration > TimeSpan.Zero ? state.Duration : _video?.Duration ?? TimeSpan.Zero;
        _timelineDrawingArea.QueueDraw();
        TryAutoSkip(state, videoId);
        UpdateManualPrompt(state, videoId);
    }

    public bool TrySkipManualSegment()
    {
        if (_lastPlaybackState is not { } state || !ManualSponsorBlockSkipEnabled(_preferences.GetPreferences()))
            return false;
        var segment = FindSponsorBlockSegmentAtPosition(_segments, state.Position);
        if (segment is null) return false;
        _seekAbsolute(segment.End.TotalSeconds);
        HideManualPrompt();
        return true;
    }

    public void Clear()
    {
        if (_disposed) return;
        CancelLoad();
        ClearState();
        _video = null;
        _videoId = null;
        _configurationKey = string.Empty;
    }

    private async Task LoadAsync(string videoId, IReadOnlyCollection<string> categories, long loadVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var segments = await _sponsorBlock.GetSegmentsAsync(videoId, categories, cancellationToken)
                .ConfigureAwait(false);
            IdleAdd(0, () =>
            {
                if (!_disposed && !cancellationToken.IsCancellationRequested && loadVersion == _loadVersion &&
                    string.Equals(_videoId, videoId, StringComparison.Ordinal))
                    SetSegments(segments);
                return false;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load SponsorBlock segments for video {VideoId}", videoId);
        }
    }
    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        IdleAdd(0, () =>
        {
            if (_disposed || GetSponsorBlockConfigurationKey(preferences) == _configurationKey) return false;
            if (!(preferences.SponsorBlockAutoSkipEnabled || preferences.SponsorBlockSegmentDisplayEnabled))
            {
                Clear();
                return false;
            }

            if (_video is null) _configurationKey = GetSponsorBlockConfigurationKey(preferences);
            else Load(_video);
            return false;
        });
    }

    private void CancelLoad()
    {
        _loadVersion++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void ClearState()
    {
        ResetManualPrompt();
        _lastPlaybackState = null;
        _lastPlaybackVideoId = null;
        _segments = [];
        _duration = TimeSpan.Zero;
        _autoSkippedSegmentIds.Clear();
        _timelineDrawingArea.QueueDraw();
    }

    private void SetSegments(IReadOnlyList<SponsorBlockSegment> segments)
    {
        if (_segments.SequenceEqual(segments)) return;
        _segments = segments;
        if (_lastPlaybackState is { } state && string.Equals(_lastPlaybackVideoId, _videoId, StringComparison.Ordinal))
            UpdateManualPrompt(state, _videoId!);
        _timelineDrawingArea.QueueDraw();
    }

    private void DrawTimeline(DrawingArea drawingArea, Context context, int width, int height)
    {
        if (!_preferences.GetPreferences().SponsorBlockSegmentDisplayEnabled || _duration <= TimeSpan.Zero ||
            width <= 0 || height <= 0)
            return;
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(_timeline, drawingArea,
            _lastPlaybackState?.Position ?? TimeSpan.Zero, _duration);
        if (trackWidth <= 0) return;
        const double rangeHeight = 10;
        var rangeY = Math.Max(0, (height - rangeHeight) / 2);
        foreach (var segment in _segments)
        {
            if (segment.Start >= _duration) continue;
            var start = PlayerTimelineGeometry.GetTrackPosition(segment.Start, _duration, trackStart, trackWidth);
            var end = PlayerTimelineGeometry.GetTrackPosition(segment.End, _duration, trackStart, trackWidth);
            var color = SponsorBlockCategories.GetColor(segment.Category);
            context.SetSourceRgba(color.Red / (double)byte.MaxValue, color.Green / (double)byte.MaxValue,
                color.Blue / (double)byte.MaxValue, color.Opacity);
            context.Rectangle(start, rangeY, Math.Max(2, end - start), rangeHeight);
            context.Fill();
        }
    }

    private void TryAutoSkip(LibMpvPlaybackState state, string videoId)
    {
        if (state.IsPaused || !_preferences.GetPreferences().SponsorBlockAutoSkipEnabled ||
            !string.Equals(_videoId, videoId, StringComparison.Ordinal)) return;
        var segment = FindSponsorBlockSegmentAtPosition(_segments, state.Position);
        if (segment is not null && _autoSkippedSegmentIds.Add(segment.Id))
        {
            Logger.Information("Auto-skipping SponsorBlock segment {SegmentId} ({Category}) for video {VideoId} to position {EndSeconds}s", segment.Id, segment.Category, videoId, segment.End.TotalSeconds);
            _seekAbsolute(segment.End.TotalSeconds);
        }
    }

    private void UpdateManualPrompt(LibMpvPlaybackState state, string videoId)
    {
        if (!ManualSponsorBlockSkipEnabled(_preferences.GetPreferences()) ||
            !string.Equals(_videoId, videoId, StringComparison.Ordinal))
        {
            ResetManualPrompt();
            return;
        }

        var segment = FindSponsorBlockSegmentAtPosition(_segments, state.Position);
        if (segment is null)
        {
            _activeManualSegment = null;
            _manualPromptAfterSeek = false;
            _manualWasPaused = state.IsPaused;
            HideManualPrompt();
            return;
        }

        var shouldShow = _manualPromptAfterSeek ||
                         !string.Equals(_activeManualSegment?.Id, segment.Id, StringComparison.Ordinal) ||
                         (state.IsPaused && !_manualWasPaused);
        _activeManualSegment = segment;
        _manualPromptAfterSeek = false;
        _manualWasPaused = state.IsPaused;
        if (shouldShow) ShowManualPrompt(segment);
    }

    private void ShowManualPrompt(SponsorBlockSegment segment)
    {
        var category = SponsorBlockCategoryLabel(segment.Category);
        _skipButton.SetLabel($"Skip {category}");
        _skipButton.SetTooltipText($"Skip {category} (Enter)");
        SetSkipButtonColor(segment.Category);
        _skipButton.SetVisible(true);
        if (_promptHideSource != 0) SourceRemove(_promptHideSource);
        _promptHideSource = TimeoutAdd(0, SkipPromptDurationMilliseconds, () =>
        {
            _promptHideSource = 0;
            if (!_disposed) _skipButton.SetVisible(false);
            return false;
        });
    }

    private void HideManualPrompt()
    {
        if (_promptHideSource != 0)
        {
            SourceRemove(_promptHideSource);
            _promptHideSource = 0;
        }

        if (!_disposed) _skipButton.SetVisible(false);
    }

    private void ResetManualPrompt()
    {
        _activeManualSegment = null;
        _manualPromptAfterSeek = false;
        _manualWasPaused = false;
        HideManualPrompt();
    }

    private void SetSkipButtonColor(string category)
    {
        var resolvedCategory =
            SponsorBlockCategories.All.Contains(category) ? category : SponsorBlockCategories.Sponsor;
        var colorClass = $"player-sponsorblock-skip-button-{resolvedCategory}";
        if (string.Equals(_skipButtonColorClass, colorClass, StringComparison.Ordinal)) return;
        if (_skipButtonColorClass is not null) _skipButton.RemoveCssClass(_skipButtonColorClass);
        _skipButton.AddCssClass(colorClass);
        _skipButtonColorClass = colorClass;
    }

    private static string GetSponsorBlockConfigurationKey(AppPreferences preferences)
    {
        if (!(preferences.SponsorBlockAutoSkipEnabled || preferences.SponsorBlockSegmentDisplayEnabled))
            return "disabled";
        var categories = preferences.SponsorBlockCategories.Where(SponsorBlockCategories.All.Contains)
            .Distinct(StringComparer.Ordinal);
        return $"{preferences.SponsorBlockAutoSkipEnabled}:{preferences.SponsorBlockSegmentDisplayEnabled}:" +
               string.Join(',', categories);
    }

    internal static SponsorBlockSegment? FindSponsorBlockSegmentAtPosition(IReadOnlyList<SponsorBlockSegment> segments,
        TimeSpan position)
    {
        return segments.FirstOrDefault(segment => position >= segment.Start && position < segment.End);
    }

    internal static bool ManualSponsorBlockSkipEnabled(AppPreferences preferences)
    {
        return preferences is { SponsorBlockSegmentDisplayEnabled: true, SponsorBlockAutoSkipEnabled: false };
    }

    private static string SponsorBlockCategoryLabel(string category)
    {
        return category switch
        {
            SponsorBlockCategories.Sponsor => "Sponsor",
            SponsorBlockCategories.SelfPromotion => "Self-promotion",
            SponsorBlockCategories.InteractionReminder => "Interaction reminder",
            SponsorBlockCategories.Intro => "Intro",
            SponsorBlockCategories.Outro => "Outro",
            SponsorBlockCategories.Preview => "Preview",
            SponsorBlockCategories.Hook => "Hook",
            SponsorBlockCategories.Filler => "Filler",
            _ => category
        };
    }
}