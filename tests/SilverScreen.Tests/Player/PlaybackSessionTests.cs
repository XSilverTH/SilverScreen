using System.Collections.Immutable;
using System.Net;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player;

namespace SilverScreen.Tests.Player;

public sealed class PlaybackSessionTests : IDisposable
{
    private readonly TrackingCoordinator _coordinator;
    private readonly FakePreferencesService _preferences;
    private readonly FakeSponsorBlockService _sponsorBlock;
    private readonly FakeWatchProgressService _watchProgress;
    private readonly FakeVideoEngagementService _videoEngagement;
    private readonly FakeYouTubeRatingService _youtubeRating;
    private readonly FakeSessionService _sessionService;
    private readonly PlaybackSession _session;

    public PlaybackSessionTests()
    {
        _coordinator = new TrackingCoordinator();
        _preferences = new FakePreferencesService();
        _sponsorBlock = new FakeSponsorBlockService();
        _watchProgress = new FakeWatchProgressService();
        _videoEngagement = new FakeVideoEngagementService();
        _youtubeRating = new FakeYouTubeRatingService();
        _sessionService = new FakeSessionService(new AccountSession(true, "Test User", "https://avatar", true));

        _session = new PlaybackSession(
            _coordinator.Coordinator,
            _preferences,
            _sponsorBlock,
            _watchProgress,
            _videoEngagement,
            _youtubeRating,
            _sessionService);
    }

    public void Dispose()
    {
        _session.Dispose();
        _coordinator.Dispose();
    }

    [Fact]
    public void Start_InitializesSessionState_AndFiresVideoChanged()
    {
        var v1 = CreateVideo("dQw4w9WgXcQ", "Never Gonna Give You Up");
        var v2 = CreateVideo("abc123_X-yZ", "Second Video");
        var request = new PlaybackRequest([v1, v2]);

        VideoSummary? changedVideo = null;
        var changedIndex = -1;
        _session.VideoChanged += (v, idx) =>
        {
            changedVideo = v;
            changedIndex = idx;
        };

        _session.Start(request);

        Assert.Same(request, _session.Request);
        Assert.Equal(v1, _session.CurrentVideo);
        Assert.Equal(0, _session.CurrentPlaylistIndex);
        Assert.False(_session.HasMedia);
        Assert.Equal(v1, changedVideo);
        Assert.Equal(0, changedIndex);
    }

    [Fact]
    public void UpdatePlayback_UpdatesCoordinator_AndAdvancesPlaylistOnVideoChange()
    {
        var v1 = CreateVideo("dQw4w9WgXcQ", "Video 1");
        var v2 = CreateVideo("abc123_X-yZ", "Video 2");
        var request = new PlaybackRequest([v1, v2]);
        _session.Start(request);

        var state1 = CreateState(0, 10, 180);
        _session.UpdatePlayback(state1);

        Assert.True(_session.HasMedia);
        Assert.Equal(state1, _session.LastPlaybackState);
        Assert.Single(_coordinator.PresenceUpdates);
        Assert.Equal(0, _coordinator.PresenceUpdates[0].PlaylistIndex);
        Assert.Equal(TimeSpan.FromSeconds(10), _coordinator.PresenceUpdates[0].Position);

        VideoSummary? changedVideo = null;
        var changedIndex = -1;
        _session.VideoChanged += (v, idx) =>
        {
            changedVideo = v;
            changedIndex = idx;
        };

        var state2 = CreateState(1, 5, 240);
        _session.UpdatePlayback(state2);

        Assert.Equal(1, _session.CurrentPlaylistIndex);
        Assert.Equal(v2, _session.CurrentVideo);
        Assert.Equal(v2, changedVideo);
        Assert.Equal(1, changedIndex);
    }

    [Fact]
    public void UpdateQueue_UpdatesRequest_AndFiresQueueUpdated()
    {
        var v1 = CreateVideo("vid1", "V1");
        var v2 = CreateVideo("vid2", "V2");
        _session.Start(new PlaybackRequest([v1, v2]));

        PlaybackRequest? updatedRequest = null;
        _session.QueueUpdated += req => updatedRequest = req;

        var v3 = CreateVideo("vid3", "V3");
        _session.UpdateQueue([v1, v2, v3]);

        Assert.NotNull(_session.Request);
        Assert.Equal(3, _session.Request.Videos.Length);
        Assert.Equal(v3, _session.Request.Videos[2]);
        Assert.Same(_session.Request, updatedRequest);
    }

    [Fact]
    public void EndSession_ResetsState_AndFiresSessionEnded()
    {
        var v1 = CreateVideo("vid1", "V1");
        _session.Start(new PlaybackRequest([v1]));

        var endedFired = false;
        _session.SessionEnded += () => endedFired = true;

        _session.EndSession();

        Assert.True(endedFired);
        Assert.Null(_session.Request);
        Assert.Null(_session.CurrentVideo);
        Assert.Equal(-1, _session.CurrentPlaylistIndex);
        Assert.False(_session.HasMedia);
    }

    [Fact]
    public void Fail_ResetsState_AndFiresFailed()
    {
        var v1 = CreateVideo("vid1", "V1");
        _session.Start(new PlaybackRequest([v1]));

        string? failedDetail = null;
        _session.Failed += detail => failedDetail = detail;

        _session.Fail("Codec error");

        Assert.Equal("Codec error", failedDetail);
        Assert.Null(_session.Request);
        Assert.Null(_session.CurrentVideo);
    }

    [Fact]
    public void Resume_AutoResume_SeeksToPosition_AndSetsRestartPrompt()
    {
        _preferences.Current = _preferences.Current with
        {
            ResumePlaybackAutomatically = true,
            ResumePlaybackOnDemand = false
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _watchProgress.ResumeFractions[video.Id] = 0.5;

        double? soughtPosition = null;
        _session.SeekRequested += (pos, _) => soughtPosition = pos;

        ResumePromptMode promptMode = ResumePromptMode.None;
        _session.ResumePromptChanged += (mode, _) => promptMode = mode;

        _session.Start(new PlaybackRequest([video]));

        var state = CreateState(0, 1, 120);
        _session.UpdatePlayback(state);

        Assert.Equal(60, soughtPosition);
        Assert.Equal(ResumePromptMode.Restart, _session.ResumePrompt);
        Assert.Equal(ResumePromptMode.Restart, promptMode);
        Assert.True(_session.CanRestart);
        Assert.False(_session.CanResume);

        soughtPosition = null;
        var restarted = _session.TryRestart();

        Assert.True(restarted);
        Assert.Equal(0, soughtPosition);
        Assert.Equal(ResumePromptMode.None, _session.ResumePrompt);
    }

    [Fact]
    public void Resume_ManualResume_SetsPrompt_AndSeeksWhenTryResumeInvoked()
    {
        _preferences.Current = _preferences.Current with
        {
            ResumePlaybackAutomatically = false,
            ResumePlaybackOnDemand = true
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _watchProgress.ResumeFractions[video.Id] = 0.25;

        double? soughtPosition = null;
        _session.SeekRequested += (pos, _) => soughtPosition = pos;

        _session.Start(new PlaybackRequest([video]));

        var state = CreateState(0, 1, 200);
        _session.UpdatePlayback(state);

        Assert.Null(soughtPosition);
        Assert.Equal(ResumePromptMode.Resume, _session.ResumePrompt);
        Assert.Equal(TimeSpan.FromSeconds(50), _session.ResumePosition);
        Assert.True(_session.CanResume);

        var resumed = _session.TryResume();

        Assert.True(resumed);
        Assert.Equal(50, soughtPosition);
        Assert.Equal(ResumePromptMode.None, _session.ResumePrompt);
    }

    [Fact]
    public void Resume_DismissResumePrompt_ClearsPromptState()
    {
        _preferences.Current = _preferences.Current with
        {
            ResumePlaybackAutomatically = false,
            ResumePlaybackOnDemand = true
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _watchProgress.ResumeFractions[video.Id] = 0.3;

        _session.Start(new PlaybackRequest([video]));
        _session.UpdatePlayback(CreateState(0, 1, 100));

        Assert.Equal(ResumePromptMode.Resume, _session.ResumePrompt);

        _session.DismissResumePrompt();

        Assert.Equal(ResumePromptMode.None, _session.ResumePrompt);
        Assert.False(_session.CanResume);
    }

    [Fact]
    public async Task SponsorBlock_AutoSkipsSegment_WhenEnabled()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockAutoSkipEnabled = true,
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        var segment = new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), SponsorBlockCategories.Sponsor);
        _sponsorBlock.Segments[video.Id] = [segment];

        double? soughtPosition = null;
        _session.SeekRequested += (pos, _) => soughtPosition = pos;

        SponsorBlockSegment? autoSkipped = null;
        _session.SponsorBlockAutoSkipped += seg => autoSkipped = seg;

        _session.Start(new PlaybackRequest([video]));

        await WaitForAsync(() => _session.SponsorBlockSegments.Count > 0);
        Assert.Single(_session.SponsorBlockSegments);

        var state = CreateState(0, 12, 100);
        _session.UpdatePlayback(state);

        Assert.Equal(25, soughtPosition);
        Assert.Equal(segment, autoSkipped);
    }

    [Fact]
    public async Task SponsorBlock_ManualPrompt_And_TrySkipManualSegment()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockAutoSkipEnabled = false,
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        var segment = new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(35), SponsorBlockCategories.Sponsor);
        _sponsorBlock.Segments[video.Id] = [segment];

        SponsorBlockSegment? promptSegment = null;
        _session.SponsorBlockPromptChanged += seg => promptSegment = seg;

        _session.Start(new PlaybackRequest([video]));

        await WaitForAsync(() => _session.SponsorBlockSegments.Count > 0);

        var state = CreateState(0, 20, 100);
        _session.UpdatePlayback(state);

        Assert.Equal(segment, _session.ActiveManualSegment);
        Assert.Equal(segment, promptSegment);

        double? soughtPosition = null;
        _session.SeekRequested += (pos, _) => soughtPosition = pos;

        var skipped = _session.TrySkipManualSegment();

        Assert.True(skipped);
        Assert.Equal(35, soughtPosition);
        Assert.Null(_session.ActiveManualSegment);
    }

    [Fact]
    public async Task EngagementAndVoting_LoadsCounts_AndHandlesVotingLifecycle()
    {
        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _videoEngagement.Engagements[video.Id] = new VideoEngagement(5000, 120);
        _youtubeRating.Ratings[video.Id] = YouTubeRatingState.None;

        _session.Start(new PlaybackRequest([video]));

        await WaitForAsync(() => _session.Engagement is not null);

        Assert.True(_session.CanVote);
        Assert.Equal(5000, _session.Engagement?.Likes);
        Assert.Equal(120, _session.Engagement?.Dislikes);
        Assert.Equal(YouTubeRatingState.None, _session.RatingState);

        var likeResult = await _session.SubmitVoteAsync(VideoVote.Like);
        Assert.True(likeResult);
        Assert.Equal(YouTubeRatingState.Like, _session.RatingState);
        Assert.Equal(YouTubeRatingState.Like, _youtubeRating.Ratings[video.Id]);

        var removeResult = await _session.SubmitVoteAsync(VideoVote.Like);
        Assert.True(removeResult);
        Assert.Equal(YouTubeRatingState.None, _session.RatingState);
        Assert.Equal(YouTubeRatingState.None, _youtubeRating.Ratings[video.Id]);

        var dislikeResult = await _session.SubmitVoteAsync(VideoVote.Dislike);
        Assert.True(dislikeResult);
        Assert.Equal(YouTubeRatingState.Dislike, _session.RatingState);
        Assert.Equal(YouTubeRatingState.Dislike, _youtubeRating.Ratings[video.Id]);
    }

    [Fact]
    public async Task EngagementAndVoting_WhenNotSignedIn_DisablesVoting()
    {
        _sessionService.Current = AccountSession.SignedOut;
        var video = CreateVideo("dQw4w9WgXcQ", "Video");

        _session.Start(new PlaybackRequest([video]));

        Assert.False(_session.CanVote);

        var result = await _session.SubmitVoteAsync(VideoVote.Like);
        Assert.False(result);
    }

    [Fact]
    public async Task PreferencesChanged_UpdatesSponsorBlockAndResume()
    {
        _preferences.Current = _preferences.Current with
        {
            ResumePlaybackOnDemand = true,
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _watchProgress.ResumeFractions[video.Id] = 0.3;
        _sponsorBlock.Segments[video.Id] = [new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), SponsorBlockCategories.Sponsor)];

        _session.Start(new PlaybackRequest([video]));
        await WaitForAsync(() => _session.SponsorBlockSegments.Count > 0);

        _session.UpdatePlayback(CreateState(0, 1, 100));
        Assert.Equal(ResumePromptMode.Resume, _session.ResumePrompt);
        Assert.Single(_session.SponsorBlockSegments);

        _preferences.UpdatePreferences(_preferences.Current with
        {
            ResumePlaybackAutomatically = false,
            ResumePlaybackOnDemand = false,
            SponsorBlockAutoSkipEnabled = false,
            SponsorBlockSegmentDisplayEnabled = false
        });

        Assert.Equal(ResumePromptMode.None, _session.ResumePrompt);
        Assert.Empty(_session.SponsorBlockSegments);
    }

    [Fact]
    public void Seek_FiresSeekRequestedWithPositionAndExactFlag()
    {
        double? requestedPos = null;
        bool? requestedExact = null;
        _session.SeekRequested += (pos, exact) =>
        {
            requestedPos = pos;
            requestedExact = exact;
        };

        _session.Seek(42.5, false);

        Assert.Equal(42.5, requestedPos);
        Assert.False(requestedExact);

        _session.Seek(100);
        Assert.Equal(100, requestedPos);
        Assert.True(requestedExact);
    }

    [Fact]
    public async Task NonYouTubeVideo_DoesNotFetchSponsorBlockOrEngagement()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockAutoSkipEnabled = true,
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var video = CreateVideo("local-file-path", "Local Video");
        _sponsorBlock.Segments[video.Id] = [new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), SponsorBlockCategories.Sponsor)];
        _videoEngagement.Engagements[video.Id] = new VideoEngagement(100, 10);

        _session.Start(new PlaybackRequest([video]));

        await Task.Delay(100);

        Assert.False(_session.CanVote);
        Assert.Null(_session.Engagement);
        Assert.Empty(_session.SponsorBlockSegments);
    }

    [Fact]
    public async Task VideoChange_CancelsPreviousAsyncLoads_AndPreventsStaleData()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var v1 = CreateVideo("dQw4w9WgXcQ", "Video 1");
        var v2 = CreateVideo("abc123_X-yZ", "Video 2");

        _videoEngagement.Engagements[v1.Id] = new VideoEngagement(100, 1);
        _videoEngagement.Engagements[v2.Id] = new VideoEngagement(200, 2);

        _sponsorBlock.Segments[v1.Id] = [new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), SponsorBlockCategories.Sponsor)];
        _sponsorBlock.Segments[v2.Id] = [new SponsorBlockSegment("seg2", TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(25), SponsorBlockCategories.Sponsor)];

        _session.Start(new PlaybackRequest([v1, v2]));

        // Immediately switch to video 2 before video 1 loads
        _session.UpdatePlayback(CreateState(1, 0, 180));

        await WaitForAsync(() => _session.Engagement?.Likes == 200);

        Assert.Equal(200, _session.Engagement?.Likes);
        Assert.Equal("abc123_X-yZ", _session.CurrentVideo?.Id);
        Assert.Single(_session.SponsorBlockSegments);
        Assert.Equal("seg2", _session.SponsorBlockSegments[0].Id);
    }

    [Fact]
    public void TryResumeAndTryRestart_WhenNoPromptActive_ReturnFalse()
    {
        _session.Start(new PlaybackRequest([CreateVideo("dQw4w9WgXcQ", "Video")]));

        Assert.False(_session.TryResume());
        Assert.False(_session.TryRestart());
    }

    [Fact]
    public async Task DismissSponsorBlockPrompt_ClearsActivePrompt()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockAutoSkipEnabled = false,
            SponsorBlockSegmentDisplayEnabled = true,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor]
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        var segment = new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), SponsorBlockCategories.Sponsor);
        _sponsorBlock.Segments[video.Id] = [segment];

        _session.Start(new PlaybackRequest([video]));
        await WaitForAsync(() => _session.SponsorBlockSegments.Count > 0);

        _session.UpdatePlayback(CreateState(0, 12, 100));
        Assert.NotNull(_session.ActiveManualSegment);

        _session.DismissSponsorBlockPrompt();
        Assert.Null(_session.ActiveManualSegment);
    }

    [Fact]
    public void TrySkipManualSegment_WhenManualSkipDisabled_ReturnsFalse()
    {
        _preferences.Current = _preferences.Current with
        {
            SponsorBlockAutoSkipEnabled = false,
            SponsorBlockSegmentDisplayEnabled = false
        };

        var video = CreateVideo("dQw4w9WgXcQ", "Video");
        _session.Start(new PlaybackRequest([video]));

        var result = _session.TrySkipManualSegment();
        Assert.False(result);
    }

    private static VideoSummary CreateVideo(string id, string title)
    {
        return new VideoSummary(id, title, "Channel", TimeSpan.FromMinutes(3), "placeholder://thumb", false);
    }

    private static LibMpvPlaybackState CreateState(
        int playlistIndex = 0,
        double positionSeconds = 0,
        double durationSeconds = 180,
        bool isPaused = false)
    {
        return new LibMpvPlaybackState(
            playlistIndex,
            TimeSpan.FromSeconds(positionSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            isPaused,
            false,
            100,
            1,
            true,
            true,
            false,
            [],
            []);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - start < timeoutMs)
        {
            await Task.Delay(20);
        }
    }

    private sealed class TrackingCoordinator : IDisposable
    {
        private readonly TrackingPresence _presence = new();
        private readonly TrackingTelemetry _telemetry = new();
        private readonly TrackingWatchProgress _watchProgress = new();
        private readonly TrackingCookieProvider _cookieProvider = new();

        public PlaybackCoordinator Coordinator { get; }
        public List<PlaybackPresenceState> PresenceUpdates => _presence.SetCalls.Select(c => c.State).ToList();

        public TrackingCoordinator()
        {
            Coordinator = new PlaybackCoordinator(_cookieProvider, _presence, _telemetry, _watchProgress);
        }

        public void Dispose()
        {
            Coordinator.Dispose();
        }
    }

    private sealed class FakePreferencesService : IPreferencesService
    {
        public AppPreferences Current { get; set; } = new();
        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences() => Current;

        public void SavePreferences(AppPreferences preferences) => UpdatePreferences(preferences);

        public void UpdatePreferences(AppPreferences preferences)
        {
            Current = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class FakeSponsorBlockService : ISponsorBlockService
    {
        public Dictionary<string, IReadOnlyList<SponsorBlockSegment>> Segments { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<SponsorBlockSegment>> GetSegmentsAsync(
            string videoId,
            IReadOnlyCollection<string> categories,
            CancellationToken cancellationToken = default)
        {
            if (Segments.TryGetValue(videoId, out var segs))
                return Task.FromResult(segs);

            return Task.FromResult<IReadOnlyList<SponsorBlockSegment>>([]);
        }
    }

    private sealed class FakeWatchProgressService : IWatchProgressService
    {
        public Dictionary<string, double?> ResumeFractions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Fractions { get; } = new(StringComparer.Ordinal);
        public List<(PlaybackRequest Request, PlaybackPresenceState State)> Updates { get; } = [];

        public event EventHandler<WatchProgress>? ProgressChanged;

        public double? GetFraction(string videoId) => Fractions.TryGetValue(videoId, out var f) ? f : null;

        public double? GetResumeFraction(string videoId) => ResumeFractions.TryGetValue(videoId, out var f) ? f : null;

        public void Update(PlaybackRequest request, PlaybackPresenceState state)
        {
            Updates.Add((request, state));
            if (request.Videos.Length > state.PlaylistIndex && state.PlaylistIndex >= 0)
            {
                var video = request.Videos[state.PlaylistIndex];
                if (state.Duration > TimeSpan.Zero)
                {
                    var frac = state.Position.TotalSeconds / state.Duration.TotalSeconds;
                    Fractions[video.Id] = frac;
                    ProgressChanged?.Invoke(this, new WatchProgress(video.Id, frac));
                }
            }
        }
    }

    private sealed class FakeVideoEngagementService : IVideoEngagementService
    {
        public Dictionary<string, VideoEngagement> Engagements { get; } = new(StringComparer.Ordinal);

        public Task<VideoEngagement?> GetEngagementAsync(string videoId, CancellationToken cancellationToken = default)
        {
            Engagements.TryGetValue(videoId, out var engagement);
            return Task.FromResult(engagement);
        }
    }

    private sealed class FakeYouTubeRatingService : IYouTubeRatingService
    {
        public Dictionary<string, YouTubeRatingState> Ratings { get; } = new(StringComparer.Ordinal);

        public Task<YouTubeRatingState> GetRatingStateAsync(string videoId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ratings.TryGetValue(videoId, out var rating) ? rating : YouTubeRatingState.None);
        }

        public Task<bool> SubmitVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default)
        {
            Ratings[videoId] = vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default)
        {
            Ratings[videoId] = YouTubeRatingState.None;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSessionService(AccountSession initial) : ISessionService
    {
        public AccountSession Current { get; set; } = initial;
        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession() => Current;

        public ManualSessionCookies? GetManualSessionCookies() => null;

        public void SetManualSession(string cookieContent, SessionCookieFormat format) { }

        public void ClearSession()
        {
            Current = AccountSession.SignedOut;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public CookieFileLease? AcquireCookieFileLease() => null;

        public CookieContainer? CreateCookieContainer() => null;
    }

    private sealed class TrackingPresence : IPlaybackPresenceService
    {
        public int ClearCount { get; private set; }
        public List<(PlaybackRequest Request, PlaybackPresenceState State)> SetCalls { get; } = [];

        public void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state) => SetCalls.Add((request, state));
        public void Clear() => ClearCount++;
        public void Dispose() { }
    }

    private sealed class TrackingTelemetry : IYouTubePlaybackTelemetryService
    {
        public List<(PlaybackRequest Request, IYouTubePlaybackTelemetrySession Session)> Sessions { get; } = [];

        public IYouTubePlaybackTelemetrySession Start(PlaybackRequest request)
        {
            var session = new TrackingTelemetrySession(request);
            Sessions.Add((request, session));
            return session;
        }

        public void Dispose() { }

        private sealed class TrackingTelemetrySession(PlaybackRequest request) : IYouTubePlaybackTelemetrySession
        {
            public PlaybackRequest Request { get; } = request;
            public List<PlaybackPresenceState> Updates { get; } = [];
            public bool IsDisposed { get; private set; }

            public void UpdateState(PlaybackPresenceState state) => Updates.Add(state);
            public void Dispose() => IsDisposed = true;
        }
    }

    private sealed class TrackingWatchProgress : IWatchProgressService
    {
        public List<(PlaybackRequest Request, PlaybackPresenceState State)> Updates { get; } = [];
        public event EventHandler<WatchProgress>? ProgressChanged { add { } remove { } }

        public double? GetFraction(string videoId) => null;
        public double? GetResumeFraction(string videoId) => null;
        public void Update(PlaybackRequest request, PlaybackPresenceState state) => Updates.Add((request, state));
    }

    private sealed class TrackingCookieProvider : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile() => null;
    }
}
