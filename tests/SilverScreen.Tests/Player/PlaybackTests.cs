using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Shell;

namespace SilverScreen.Tests.Player;

public sealed class PlaybackTests
{
    [Fact]
    public void ActivePlaybackLifecycleRestoresTheMostRecentRemainingSession()
    {
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(new TestPreferences(), null, presence);
        var firstRequest = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);
        var secondRequest = new PlaybackRequest([CreateVideo("dQw4w9WgXcQ")]);
        var thirdRequest = new PlaybackRequest([CreateVideo("M7lc1UVf-VE")]);
        var firstStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var secondStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thirdStartedAt = DateTimeOffset.UtcNow;

        var firstId = service.RegisterActivePlayback(firstRequest);
        service.UpdateActivePlayback(firstId, PlayingState(firstStartedAt));
        var secondId = service.RegisterActivePlayback(secondRequest);
        service.UpdateActivePlayback(secondId, PlayingState(secondStartedAt));
        var thirdId = service.RegisterActivePlayback(thirdRequest);
        service.UpdateActivePlayback(thirdId, PlayingState(thirdStartedAt));
        service.CompleteActivePlayback(secondId);

        Assert.Equal(3, presence.SetCalls.Count);
        Assert.Equal(thirdRequest, presence.SetCalls[^1].Request);

        service.CompleteActivePlayback(thirdId);

        Assert.Equal(firstRequest, presence.SetCalls[^1].Request);
        Assert.Equal(firstStartedAt, presence.SetCalls[^1].State.ObservedAt);

        service.CompleteActivePlayback(firstId);
        Assert.Equal(1, presence.ClearCount);
        service.CompleteActivePlayback(999);
        Assert.Equal(1, presence.ClearCount);
    }

    [Fact]
    public void PlaybackCoordinator_TracksActivePlaybackTelemetryAndPresence()
    {
        var presence = new TrackingPresence();
        var telemetry = new TrackingTelemetry();
        var watchProgress = new TrackingWatchProgress();
        using var coordinator = new PlaybackCoordinator(null, presence, telemetry, watchProgress);

        var request = new PlaybackRequest([CreateVideo("vid1"), CreateVideo("vid2")]);
        var playbackId = coordinator.RegisterActivePlayback(request);
        Assert.True(playbackId > 0);
        Assert.Single(telemetry.Sessions);
        Assert.Equal(request, telemetry.Sessions[0].Request);

        var state = PlayingState(DateTimeOffset.UtcNow);
        coordinator.UpdateActivePlayback(playbackId, state);

        Assert.Single(presence.SetCalls);
        Assert.Equal(request, presence.SetCalls[0].Request);
        Assert.Equal(state, presence.SetCalls[0].State);

        Assert.Single(telemetry.Sessions[0].Session.Updates);
        Assert.Equal(state, telemetry.Sessions[0].Session.Updates[0]);

        Assert.Single(watchProgress.Updates);
        Assert.Equal(request, watchProgress.Updates[0].Request);
        Assert.Equal(state, watchProgress.Updates[0].State);
    }

    [Fact]
    public void PlaybackCoordinator_RestoresMostRecentPlaybackPresenceOnCompletion()
    {
        var presence = new TrackingPresence();
        using var coordinator = new PlaybackCoordinator(null, presence);

        var req1 = new PlaybackRequest([CreateVideo("vid1")]);
        var req2 = new PlaybackRequest([CreateVideo("vid2")]);
        var req3 = new PlaybackRequest([CreateVideo("vid3")]);

        var time1 = DateTimeOffset.UtcNow.AddMinutes(-2);
        var time2 = DateTimeOffset.UtcNow.AddMinutes(-1);
        var time3 = DateTimeOffset.UtcNow;

        var id1 = coordinator.RegisterActivePlayback(req1);
        coordinator.UpdateActivePlayback(id1, PlayingState(time1));

        var id2 = coordinator.RegisterActivePlayback(req2);
        coordinator.UpdateActivePlayback(id2, PlayingState(time2));

        var id3 = coordinator.RegisterActivePlayback(req3);
        coordinator.UpdateActivePlayback(id3, PlayingState(time3));

        coordinator.CompleteActivePlayback(id2);
        Assert.Equal(req3, presence.SetCalls[^1].Request);

        coordinator.CompleteActivePlayback(id3);
        Assert.Equal(req1, presence.SetCalls[^1].Request);
        Assert.Equal(time1, presence.SetCalls[^1].State.ObservedAt);

        coordinator.CompleteActivePlayback(id1);
        Assert.Equal(1, presence.ClearCount);
    }

    [Fact]
    public void PlaybackCoordinator_AcquiresCookieLeaseFromProvider()
    {
        var cookieProvider = new TrackingCookieProvider();
        using var coordinator = new PlaybackCoordinator(cookieProvider);

        var lease = coordinator.AcquireCookieFileLease();
        Assert.Null(lease);
        Assert.Equal(1, cookieProvider.CallCount);
    }

    [Fact]
    public void PlaybackCoordinator_PlaylistHelpers_ResolvesVideosAndQueueChanges()
    {
        var v1 = CreateVideo("vid1");
        var v2 = CreateVideo("vid2");
        var request = new PlaybackRequest([v1, v2]);

        Assert.Equal(v1, PlaybackCoordinator.GetVideoAt(request, 0));
        Assert.Equal(v2, PlaybackCoordinator.GetVideoAt(request, 1));
        Assert.Null(PlaybackCoordinator.GetVideoAt(request, -1));
        Assert.Null(PlaybackCoordinator.GetVideoAt(request, 2));
        Assert.Null(PlaybackCoordinator.GetVideoAt(null, 0));

        Assert.True(PlaybackCoordinator.TryResolveVideoChange(request, 0, "vid1", 1, out var resolvedVideo,
            out var changed));
        Assert.Equal(v2, resolvedVideo);
        Assert.True(changed);

        Assert.True(PlaybackCoordinator.TryResolveVideoChange(request, 0, "vid1", 0, out var sameVideo,
            out var sameChanged));
        Assert.Equal(v1, sameVideo);
        Assert.False(sameChanged);

        Assert.False(PlaybackCoordinator.TryResolveVideoChange(request, 0, "vid1", 5, out _, out _));

        var v3 = CreateVideo("vid3");
        var updated = PlaybackCoordinator.UpdateQueue([v1, v2, v3]);
        Assert.Equal(3, updated.Videos.Length);
        Assert.Equal("vid3", updated.Videos[2].Id);
    }

    [Fact]
    public void PlaybackCoordinator_Dispose_DisposesActiveTelemetrySessionsAndClearsPresence()
    {
        var presence = new TrackingPresence();
        var telemetry = new TrackingTelemetry();
        var coordinator = new PlaybackCoordinator(null, presence, telemetry);

        var request = new PlaybackRequest([CreateVideo("vid1")]);
        coordinator.RegisterActivePlayback(request);

        Assert.Single(telemetry.Sessions);
        Assert.False(telemetry.Sessions[0].Session.IsDisposed);

        coordinator.Dispose();

        Assert.True(telemetry.Sessions[0].Session.IsDisposed);
        Assert.Equal(1, presence.ClearCount);
    }


    [Fact]
    public void MpvIpcProtocolAppliesObservedPlaybackProperties()
    {
        var state = PlaybackPresenceState.CreateInitial(DateTimeOffset.UtcNow);

        Assert.True(MpvIpcPlaybackProtocol.TryApply(
            """{"request_id":100,"data":42.5}""", ref state, out var positionProperty));
        Assert.Equal("time-pos", positionProperty);
        Assert.True(MpvIpcPlaybackProtocol.TryApply(
            """{"event":"property-change","name":"pause","data":false}""", ref state, out var pauseProperty));
        Assert.Equal("pause", pauseProperty);
        Assert.True(MpvIpcPlaybackProtocol.TryApply(
            """{"event":"property-change","name":"duration","data":180}""", ref state, out _));
        Assert.True(MpvIpcPlaybackProtocol.TryApply(
            """{"event":"property-change","name":"playlist-pos","data":1}""", ref state, out _));

        Assert.Equal(TimeSpan.FromSeconds(42.5), state.Position);
        Assert.Equal(TimeSpan.FromMinutes(3), state.Duration);
        Assert.False(state.IsPaused);
        Assert.Equal(1, state.PlaylistIndex);
    }

    [Fact]
    public void DesktopPlaybackSnapshotUpdatesMprisForPlaylistPositions()
    {
        var video1 = CreateVideo("vid1");
        var video2 = CreateVideo("vid2");
        var video3 = CreateVideo("vid3");
        var request = new PlaybackRequest([video1, video2, video3]);

        var firstState = new LibMpvPlaybackState(0, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), false, false,
            100, 1, true, true, false, [], []);
        var firstSnapshot = DesktopMediaIntegration.DesktopPlaybackSnapshot.Create(request, firstState);
        Assert.True(firstSnapshot.CanGoNext);
        Assert.False(firstSnapshot.CanGoPrevious);
        Assert.Equal("Video vid1", firstSnapshot.Metadata["xesam:title"].GetString());

        var middleState = new LibMpvPlaybackState(1, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(3), false, false,
            100, 1, true, true, false, [], []);
        var middleSnapshot = DesktopMediaIntegration.DesktopPlaybackSnapshot.Create(request, middleState);
        Assert.True(middleSnapshot.CanGoNext);
        Assert.True(middleSnapshot.CanGoPrevious);
        Assert.Equal("Video vid2", middleSnapshot.Metadata["xesam:title"].GetString());

        var lastState = new LibMpvPlaybackState(2, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), false, false, 100,
            1, true, true, false, [], []);
        var lastSnapshot = DesktopMediaIntegration.DesktopPlaybackSnapshot.Create(request, lastState);
        Assert.False(lastSnapshot.CanGoNext);
        Assert.True(lastSnapshot.CanGoPrevious);
        Assert.Equal("Video vid3", lastSnapshot.Metadata["xesam:title"].GetString());
    }

    [Fact]
    public async Task PlaybackModeRoutingServiceRoutesToEmbeddedPlayerPresenterWhenConfigured()
    {
        var embedded = new TrackingEmbeddedPresenter();
        var external = new TrackingPlaybackService();
        var preferences = new TestPreferences(new AppPreferences { PlaybackBackend = PlaybackBackends.EmbeddedPlayer });
        var routing = new PlaybackModeRoutingService(preferences, external, embedded);
        var request = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);

        var result = await routing.PlayAsync(request);

        Assert.Equal("Embedded presenter called.", result);
        Assert.Single(embedded.Requests);
        Assert.Equal(request, embedded.Requests[0]);
        Assert.Empty(external.Requests);
    }

    [Fact]
    public async Task PlaybackModeRoutingServiceRoutesToExternalMpvWhenConfigured()
    {
        var embedded = new TrackingEmbeddedPresenter();
        var external = new TrackingPlaybackService();
        var preferences = new TestPreferences(new AppPreferences { PlaybackBackend = PlaybackBackends.ExternalMpv });
        var routing = new PlaybackModeRoutingService(preferences, external, embedded);
        var request = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);

        var result = await routing.PlayAsync(request);

        Assert.Equal("External playback called.", result);
        Assert.Empty(embedded.Requests);
        Assert.Single(external.Requests);
        Assert.Equal(request, external.Requests[0]);
    }


    private static PlaybackPresenceState PlayingState(DateTimeOffset observedAt)
    {
        return new PlaybackPresenceState(0, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), false, 1, observedAt);
    }

    private static VideoSummary CreateVideo(string id, string? watchUrl = null)
    {
        return new VideoSummary(id, $"Video {id}", "Test Channel", TimeSpan.FromMinutes(3), "placeholder://test", false,
            watchUrl);
    }

    private sealed class TrackingPresence : IPlaybackPresenceService
    {
        public int ClearCount { get; private set; }
        public List<(PlaybackRequest Request, PlaybackPresenceState State)> SetCalls { get; } = [];

        public void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state)
        {
            SetCalls.Add((request, state));
        }

        public void Clear()
        {
            ClearCount++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingTelemetry : IYouTubePlaybackTelemetryService
    {
        public List<(PlaybackRequest Request, TrackingTelemetrySession Session)> Sessions { get; } = [];

        public IYouTubePlaybackTelemetrySession Start(PlaybackRequest request)
        {
            var session = new TrackingTelemetrySession(request);
            Sessions.Add((request, session));
            return session;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingTelemetrySession(PlaybackRequest request) : IYouTubePlaybackTelemetrySession
    {
        public PlaybackRequest Request { get; } = request;
        public List<PlaybackPresenceState> Updates { get; } = [];
        public bool IsDisposed { get; private set; }

        public void UpdateState(PlaybackPresenceState state)
        {
            Updates.Add(state);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TrackingWatchProgress : IWatchProgressService
    {
        public List<(PlaybackRequest Request, PlaybackPresenceState State)> Updates { get; } = [];

        public event EventHandler<WatchProgress>? ProgressChanged
        {
            add { }
            remove { }
        }

        public double? GetFraction(string videoId)
        {
            return null;
        }

        public double? GetResumeFraction(string videoId)
        {
            return null;
        }

        public void Update(PlaybackRequest request, PlaybackPresenceState state)
        {
            Updates.Add((request, state));
        }
    }

    private sealed class TrackingCookieProvider(CookieFileLease? lease = null) : ICookieFileProvider
    {
        public int CallCount { get; private set; }

        public CookieFileLease? CreateCookieFile()
        {
            CallCount++;
            return lease;
        }
    }

    private sealed class TestPreferences(AppPreferences? preferences = null) : IPreferencesService
    {
        private readonly AppPreferences _preferences = preferences ?? new AppPreferences();

        public event EventHandler<AppPreferences>? PreferencesChanged
        {
            add { }
            remove { }
        }

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }

        public void SavePreferences(AppPreferences preferences)
        {
        }
    }

    private sealed class TrackingEmbeddedPresenter : IEmbeddedPlayerPresenter
    {
        public List<PlaybackRequest> Requests { get; } = [];

        public Task<string> PresentAsync(PlaybackRequest request)
        {
            Requests.Add(request);
            return Task.FromResult("Embedded presenter called.");
        }
    }

    private sealed class TrackingPlaybackService : IPlaybackService
    {
        public List<PlaybackRequest> Requests { get; } = [];

        public Task<string> PlayAsync(PlaybackRequest request)
        {
            Requests.Add(request);
            return Task.FromResult("External playback called.");
        }
    }
}