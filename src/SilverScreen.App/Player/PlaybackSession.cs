using System.Collections.Immutable;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player.Controllers;
using SilverScreen.Shell;

namespace SilverScreen.Player;

/// <summary>
///     Deep playback engine managing end-to-end playback lifecycle and sidecars:
///     telemetry, presence, cookie leasing, SponsorBlock auto-skipping and prompts,
///     watch progress and resume calculation, video engagement/ratings, and playlist progression.
/// </summary>
internal sealed class PlaybackSession : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlaybackSession>();

    private readonly PlaybackCoordinator _coordinator;
    private readonly DesktopMediaIntegration? _desktopMedia;
    private readonly IPreferencesService _preferences;
    private readonly ISponsorBlockService _sponsorBlock;
    private readonly IWatchProgressService _watchProgress;
    private readonly IVideoEngagementService _videoEngagement;
    private readonly IYouTubeRatingService _youtubeRating;
    private readonly ISessionService _session;

    private readonly HashSet<string> _autoSkippedSegmentIds = new(StringComparer.Ordinal);
    private SponsorBlockSegment? _activeManualSegment;
    private CookieFileLease? _cookieFile;
    private bool _disposed;
    private bool _handledResumeForCurrentVideo;
    private bool _hadSeek;
    private TimeSpan _lastKnownDuration;
    private string? _lastPlaybackVideoId;
    private CancellationTokenSource? _loadCts;
    private long _loadVersion;
    private long _playbackId;
    private double? _resumeFraction;
    private string _sponsorBlockConfigurationKey = string.Empty;
    private bool _wasPaused;

    public PlaybackSession(
        PlaybackCoordinator coordinator,
        IPreferencesService preferences,
        ISponsorBlockService sponsorBlock,
        IWatchProgressService watchProgress,
        IVideoEngagementService videoEngagement,
        IYouTubeRatingService youtubeRating,
        ISessionService session,
        DesktopMediaIntegration? desktopMedia = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _sponsorBlock = sponsorBlock ?? throw new ArgumentNullException(nameof(sponsorBlock));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoEngagement = videoEngagement ?? throw new ArgumentNullException(nameof(videoEngagement));
        _youtubeRating = youtubeRating ?? throw new ArgumentNullException(nameof(youtubeRating));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _desktopMedia = desktopMedia;

        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public PlaybackSession(
        PlayerDependencies dependencies,
        DesktopMediaIntegration? desktopMedia = null)
        : this(
            (dependencies ?? throw new ArgumentNullException(nameof(dependencies))).PlaybackCoordinator,
            dependencies.Preferences,
            dependencies.SponsorBlock,
            dependencies.WatchProgress,
            dependencies.VideoEngagement,
            dependencies.YouTubeRating,
            dependencies.Session,
            desktopMedia)
    {
    }

    public PlaybackRequest? Request { get; private set; }
    public VideoSummary? CurrentVideo { get; private set; }
    public int CurrentPlaylistIndex { get; private set; } = -1;
    public string? CookieFilePath => _cookieFile?.Path;
    public bool HasMedia { get; private set; }
    public LibMpvPlaybackState? LastPlaybackState { get; private set; }

    public VideoEngagement? Engagement { get; private set; }
    public YouTubeRatingState RatingState { get; private set; } = YouTubeRatingState.None;
    public bool CanVote => CurrentVideo is { } v &&
                           PlaybackRequest.LooksLikeYouTubeVideoId(v.Id) &&
                           _session.GetCurrentSession().IsSignedIn;

    public IReadOnlyList<SponsorBlockSegment> SponsorBlockSegments { get; private set; } = [];
    public SponsorBlockSegment? ActiveManualSegment { get; private set; }

    public ResumePromptMode ResumePrompt { get; private set; } = ResumePromptMode.None;
    public TimeSpan ResumePosition { get; private set; } = TimeSpan.Zero;
    public bool CanResume => ResumePrompt == ResumePromptMode.Resume;
    public bool CanRestart => ResumePrompt == ResumePromptMode.Restart;

    public event Action<VideoSummary, int>? VideoChanged;
    public event Action? SessionEnded;
    public event Action<string>? Failed;
    public event Action<PlaybackRequest>? QueueUpdated;
    public event Action<double, bool>? SeekRequested;
    public event Action<VideoEngagement?>? EngagementChanged;
    public event Action<YouTubeRatingState>? RatingStateChanged;
    public event Action<IReadOnlyList<SponsorBlockSegment>>? SponsorBlockSegmentsChanged;
    public event Action<SponsorBlockSegment?>? SponsorBlockPromptChanged;
    public event Action<SponsorBlockSegment>? SponsorBlockAutoSkipped;
    public event Action<ResumePromptMode, TimeSpan>? ResumePromptChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        Reset();
    }

    public void Start(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed) return;

        Reset();
        Request = request;
        CurrentPlaylistIndex = 0;
        CurrentVideo = PlaybackCoordinator.GetVideoAt(request, 0);
        HasMedia = false;
        _playbackId = _coordinator.RegisterActivePlayback(request);
        _cookieFile = _coordinator.AcquireCookieFileLease();

        if (CurrentVideo is not null)
        {
            LoadVideo(CurrentVideo);
            VideoChanged?.Invoke(CurrentVideo, 0);
        }
    }

    public void UpdatePlayback(LibMpvPlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed) return;

        HasMedia = state.HasMedia;

        if (Request is not null && state.HasMedia && _playbackId != 0)
        {
            var playbackState = new PlaybackPresenceState(
                state.PlaylistIndex,
                state.Position,
                state.Duration,
                state.IsPaused,
                state.Speed,
                DateTimeOffset.UtcNow);

            _coordinator.UpdateActivePlayback(_playbackId, playbackState);
        }

        _desktopMedia?.UpdatePlayback(Request, state);

        if (PlaybackCoordinator.TryResolveVideoChange(
                Request,
                CurrentPlaylistIndex,
                CurrentVideo?.Id,
                state.PlaylistIndex,
                out var video,
                out var videoChanged) && video is not null)
        {
            CurrentPlaylistIndex = state.PlaylistIndex;
            CurrentVideo = video;

            if (videoChanged)
            {
                LoadVideo(video);
                VideoChanged?.Invoke(video, state.PlaylistIndex);
            }
        }

        if (state.HasMedia && CurrentVideo is not null)
        {
            EvaluateResume(state);
            EvaluateSponsorBlock(state);
        }

        _lastPlaybackVideoId = CurrentVideo?.Id;
        _wasPaused = state.IsPaused;
        LastPlaybackState = state;
    }

    public void UpdateQueue(ImmutableArray<VideoSummary> newVideos)
    {
        if (_disposed || Request is null) return;
        Request = PlaybackCoordinator.UpdateQueue(newVideos);
        QueueUpdated?.Invoke(Request);
    }

    public void EndSession()
    {
        if (_disposed) return;
        Reset();
        SessionEnded?.Invoke();
    }

    public void Fail(string detail)
    {
        if (_disposed) return;
        Reset();
        Failed?.Invoke(detail);
    }

    public void Seek(double positionSeconds, bool exact = true)
    {
        if (_disposed) return;
        SeekRequested?.Invoke(positionSeconds, exact);
    }

    public bool TryResume()
    {
        if (_disposed || ResumePrompt != ResumePromptMode.Resume || ResumePosition <= TimeSpan.Zero)
            return false;

        Seek(ResumePosition.TotalSeconds);
        DismissResumePrompt();
        return true;
    }

    public bool TryRestart()
    {
        if (_disposed || ResumePrompt != ResumePromptMode.Restart)
            return false;

        Seek(0);
        DismissResumePrompt();
        return true;
    }

    public void DismissResumePrompt()
    {
        if (_disposed || ResumePrompt == ResumePromptMode.None) return;
        ResumePrompt = ResumePromptMode.None;
        ResumePosition = TimeSpan.Zero;
        ResumePromptChanged?.Invoke(ResumePromptMode.None, TimeSpan.Zero);
    }

    public bool TrySkipManualSegment()
    {
        if (_disposed) return false;
        var prefs = _preferences.GetPreferences();
        if (!PlayerTimelineEngine.ManualSponsorBlockSkipEnabled(prefs)) return false;

        var segment = ActiveManualSegment ??
                      (LastPlaybackState is { } st
                          ? PlayerTimelineEngine.FindSponsorBlockSegmentAt(SponsorBlockSegments, st.Position)
                          : null);

        if (segment is null) return false;

        Seek(segment.End.TotalSeconds);
        DismissSponsorBlockPrompt();
        return true;
    }

    public void DismissSponsorBlockPrompt()
    {
        if (_disposed || ActiveManualSegment is null) return;
        _activeManualSegment = null;
        ActiveManualSegment = null;
        SponsorBlockPromptChanged?.Invoke(null);
    }

    public async Task<bool> SubmitVoteAsync(VideoVote vote, CancellationToken cancellationToken = default)
    {
        if (_disposed || CurrentVideo is not { } video || !CanVote) return false;

        var removeVote = RatingState == (vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike);
        var version = _loadVersion;

        bool succeeded;
        try
        {
            succeeded = removeVote
                ? await _youtubeRating.RemoveVoteAsync(video.Id, vote, cancellationToken).ConfigureAwait(false)
                : await _youtubeRating.SubmitVoteAsync(video.Id, vote, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to submit vote {Vote} for {VideoId}", vote, video.Id);
            return false;
        }

        if (!succeeded || _disposed || version != _loadVersion || !string.Equals(CurrentVideo?.Id, video.Id, StringComparison.Ordinal))
            return succeeded;

        RatingState = removeVote
            ? YouTubeRatingState.None
            : (vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike);

        RatingStateChanged?.Invoke(RatingState);
        FetchEngagementAsync(video.Id, version, CancellationToken.None).FireAndForget(Logger);

        return true;
    }

    public void SubmitVote(VideoVote vote)
    {
        if (_disposed) return;
        var token = _loadCts?.Token ?? CancellationToken.None;
        SubmitVoteAsync(vote, token).FireAndForget(Logger);
    }

    private void LoadVideo(VideoSummary video)
    {
        CancelVideoLoads();

        var version = ++_loadVersion;
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _handledResumeForCurrentVideo = false;
        _lastKnownDuration = TimeSpan.Zero;
        _resumeFraction = _watchProgress.GetResumeFraction(video.Id);
        ResumePrompt = ResumePromptMode.None;
        ResumePosition = TimeSpan.Zero;
        ResumePromptChanged?.Invoke(ResumePromptMode.None, TimeSpan.Zero);

        _activeManualSegment = null;
        ActiveManualSegment = null;
        _hadSeek = false;
        _wasPaused = false;
        _autoSkippedSegmentIds.Clear();
        SponsorBlockSegments = [];
        SponsorBlockSegmentsChanged?.Invoke([]);
        SponsorBlockPromptChanged?.Invoke(null);

        Engagement = null;
        RatingState = YouTubeRatingState.None;
        EngagementChanged?.Invoke(null);
        RatingStateChanged?.Invoke(YouTubeRatingState.None);

        if (!PlaybackRequest.LooksLikeYouTubeVideoId(video.Id)) return;

        var prefs = _preferences.GetPreferences();
        _sponsorBlockConfigurationKey = PlayerTimelineEngine.GetSponsorBlockConfigurationKey(prefs);

        if (prefs.SponsorBlockAutoSkipEnabled || prefs.SponsorBlockSegmentDisplayEnabled)
        {
            var categories = prefs.SponsorBlockCategories
                .Where(SponsorBlockCategories.All.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (categories.Length > 0)
                FetchSponsorBlockAsync(video.Id, categories, version, cts.Token).FireAndForget(Logger);
        }

        FetchEngagementAsync(video.Id, version, cts.Token).FireAndForget(Logger);
        FetchRatingAsync(video.Id, version, cts.Token).FireAndForget(Logger);
    }

    private void EvaluateResume(LibMpvPlaybackState state)
    {
        if (_handledResumeForCurrentVideo || state.Duration <= TimeSpan.Zero) return;

        _handledResumeForCurrentVideo = true;
        _lastKnownDuration = state.Duration;

        var prefs = _preferences.GetPreferences();
        var promptState = PlayerTimelineEngine.GetResumePromptState(
            _resumeFraction,
            state.Duration,
            prefs.ResumePlaybackAutomatically,
            prefs.ResumePlaybackOnDemand,
            out var resumePos);

        switch (promptState)
        {
            case ResumePromptState.AutoResume:
                Seek(resumePos.TotalSeconds);
                ResumePrompt = ResumePromptMode.Restart;
                ResumePosition = TimeSpan.Zero;
                ResumePromptChanged?.Invoke(ResumePromptMode.Restart, TimeSpan.Zero);
                break;
            case ResumePromptState.ManualResume:
                ResumePrompt = ResumePromptMode.Resume;
                ResumePosition = resumePos;
                ResumePromptChanged?.Invoke(ResumePromptMode.Resume, resumePos);
                break;
            case ResumePromptState.None:
            default:
                ResumePrompt = ResumePromptMode.None;
                ResumePosition = TimeSpan.Zero;
                break;
        }
    }

    private void EvaluateSponsorBlock(LibMpvPlaybackState state)
    {
        if (CurrentVideo is null) return;

        if (LastPlaybackState is { } prev &&
            string.Equals(_lastPlaybackVideoId, CurrentVideo.Id, StringComparison.Ordinal) &&
            Math.Abs((state.Position - prev.Position).TotalSeconds) > 1)
        {
            _hadSeek = true;
        }

        var prefs = _preferences.GetPreferences();

        if (PlayerTimelineEngine.ShouldAutoSkip(
                state.Position,
                SponsorBlockSegments,
                state.IsPaused,
                prefs.SponsorBlockAutoSkipEnabled,
                _autoSkippedSegmentIds,
                out var skipSegment))
        {
            Logger.Information(
                "Auto-skipping SponsorBlock segment {SegmentId} ({Category}) for video {VideoId} to position {EndSeconds}s",
                skipSegment.Id, skipSegment.Category, CurrentVideo.Id, skipSegment.End.TotalSeconds);

            Seek(skipSegment.End.TotalSeconds);
            SponsorBlockAutoSkipped?.Invoke(skipSegment);
        }

        if (PlayerTimelineEngine.ManualSponsorBlockSkipEnabled(prefs))
        {
            var candidate = PlayerTimelineEngine.FindSponsorBlockSegmentAt(SponsorBlockSegments, state.Position);
            if (candidate is null)
            {
                if (_activeManualSegment is not null)
                {
                    _activeManualSegment = null;
                    ActiveManualSegment = null;
                    _hadSeek = false;
                    _wasPaused = state.IsPaused;
                    SponsorBlockPromptChanged?.Invoke(null);
                }
            }
            else
            {
                var shouldShow = PlayerTimelineEngine.ShouldShowManualPrompt(
                    _activeManualSegment,
                    candidate,
                    state.IsPaused,
                    _wasPaused,
                    _hadSeek);

                _activeManualSegment = candidate;
                ActiveManualSegment = candidate;
                _hadSeek = false;
                _wasPaused = state.IsPaused;

                if (shouldShow)
                    SponsorBlockPromptChanged?.Invoke(candidate);
            }
        }
        else if (_activeManualSegment is not null)
        {
            _activeManualSegment = null;
            ActiveManualSegment = null;
            _hadSeek = false;
            _wasPaused = state.IsPaused;
            SponsorBlockPromptChanged?.Invoke(null);
        }
    }

    private async Task FetchSponsorBlockAsync(
        string videoId,
        IReadOnlyCollection<string> categories,
        long version,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SponsorBlockSegment> segments;
        try
        {
            segments = await _sponsorBlock.GetSegmentsAsync(videoId, categories, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load SponsorBlock segments for {VideoId}", videoId);
            return;
        }

        if (_disposed || cancellationToken.IsCancellationRequested || version != _loadVersion ||
            !string.Equals(CurrentVideo?.Id, videoId, StringComparison.Ordinal))
            return;

        SponsorBlockSegments = segments;
        SponsorBlockSegmentsChanged?.Invoke(segments);

        if (LastPlaybackState is { } state && string.Equals(_lastPlaybackVideoId, videoId, StringComparison.Ordinal))
            EvaluateSponsorBlock(state);
    }

    private async Task FetchEngagementAsync(string videoId, long version, CancellationToken cancellationToken)
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
        catch (Exception ex)
        {
            Logger.Debug(ex, "Unable to load engagement for {VideoId}", videoId);
            return;
        }

        if (_disposed || cancellationToken.IsCancellationRequested || version != _loadVersion ||
            !string.Equals(CurrentVideo?.Id, videoId, StringComparison.Ordinal))
            return;

        Engagement = engagement;
        EngagementChanged?.Invoke(engagement);
    }

    private async Task FetchRatingAsync(string videoId, long version, CancellationToken cancellationToken)
    {
        YouTubeRatingState rating;
        try
        {
            rating = await _youtubeRating.GetRatingStateAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Unable to load rating for {VideoId}", videoId);
            return;
        }

        if (_disposed || cancellationToken.IsCancellationRequested || version != _loadVersion ||
            !string.Equals(CurrentVideo?.Id, videoId, StringComparison.Ordinal))
            return;

        RatingState = rating;
        RatingStateChanged?.Invoke(rating);
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        if (_disposed) return;

        if (!preferences.ResumePlaybackAutomatically && !preferences.ResumePlaybackOnDemand)
        {
            DismissResumePrompt();
        }

        var newKey = PlayerTimelineEngine.GetSponsorBlockConfigurationKey(preferences);
        if (newKey == _sponsorBlockConfigurationKey) return;
        _sponsorBlockConfigurationKey = newKey;

        if (!preferences.SponsorBlockAutoSkipEnabled && !preferences.SponsorBlockSegmentDisplayEnabled)
        {
            SponsorBlockSegments = [];
            _activeManualSegment = null;
            ActiveManualSegment = null;
            SponsorBlockSegmentsChanged?.Invoke([]);
            SponsorBlockPromptChanged?.Invoke(null);
            return;
        }

        if (CurrentVideo is not null && PlaybackRequest.LooksLikeYouTubeVideoId(CurrentVideo.Id))
        {
            var categories = preferences.SponsorBlockCategories
                .Where(SponsorBlockCategories.All.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (categories.Length > 0 && _loadCts is { } cts)
                FetchSponsorBlockAsync(CurrentVideo.Id, categories, _loadVersion, cts.Token).FireAndForget(Logger);
        }
    }

    private void CancelVideoLoads()
    {
        _loadVersion++;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    private void Reset()
    {
        CancelVideoLoads();
        ReleaseResources();
        Request = null;
        CurrentVideo = null;
        CurrentPlaylistIndex = -1;
        HasMedia = false;
        LastPlaybackState = null;
        _lastPlaybackVideoId = null;
        Engagement = null;
        RatingState = YouTubeRatingState.None;
        SponsorBlockSegments = [];
        _activeManualSegment = null;
        ActiveManualSegment = null;
        _autoSkippedSegmentIds.Clear();
        ResumePrompt = ResumePromptMode.None;
        ResumePosition = TimeSpan.Zero;
        _resumeFraction = null;
        _handledResumeForCurrentVideo = false;
    }

    private void ReleaseResources()
    {
        if (_playbackId != 0)
        {
            _coordinator.CompleteActivePlayback(_playbackId);
            _playbackId = 0;
        }

        _cookieFile?.Dispose();
        _cookieFile = null;
        _desktopMedia?.ClearPlayback();
    }
}
