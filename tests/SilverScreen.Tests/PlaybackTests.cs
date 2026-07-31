using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public void ActivePlaybackLifecycleRestoresTheMostRecentRemainingSession()
    {
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, presence);
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
    public void ExternalMpvForwardsPlaybackStateToYouTubeTelemetry()
    {
        var telemetry = new TrackingTelemetry();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, null,
            telemetry);
        var request = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);
        var playbackId = service.RegisterActivePlayback(request);
        var state = PlayingState(DateTimeOffset.UtcNow);

        service.UpdateActivePlayback(playbackId, state);
        service.CompleteActivePlayback(playbackId);

        var session = Assert.Single(telemetry.Sessions);
        Assert.Equal(request, telemetry.Requests[0]);
        Assert.Equal([state], session.States);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void ExternalMpvForwardsPlaybackStateToWatchProgress()
    {
        var watchProgress = new TrackingWatchProgress();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, null, null,
            watchProgress);
        var request = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);
        var playbackId = service.RegisterActivePlayback(request);
        var state = PlayingState(DateTimeOffset.UtcNow);

        service.UpdateActivePlayback(playbackId, state);

        Assert.Equal([(request, state)], watchProgress.Updates);
    }

    [Fact]
    public void MpvCommandBuilderPassesOrderedPlaylistUrlsAsSeparateArguments()
    {
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([
                CreateVideo("abc123_X-yZ"),
                CreateVideo("dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ"),
                CreateVideo("M7lc1UVf-VE")
            ]),
            new PlaybackOptions { VideoQuality = "720p" },
            "/tmp/silverscreen-cookies/cookies.txt");

        Assert.Equal(
            [
                "--fs",
                "--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt",
                "--ytdl-format=bestvideo[height<=720]+bestaudio/best[height<=720]",
                "--keep-open=yes",
                "https://www.youtube.com/watch?v=abc123_X-yZ",
                "https://youtu.be/dQw4w9WgXcQ",
                "https://www.youtube.com/watch?v=M7lc1UVf-VE"
            ],
            command.Arguments);
    }

    [Fact]
    public void MpvCommandBuilderKeepsCurrentVideoOpenWhenAutoAdvanceIsDisabled()
    {
        var command = MpvCommandBuilder.Build(new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions { AutoAdvanceNextVideo = false });

        Assert.Contains("--keep-open=always", command.Arguments);
    }

    [Fact]
    public void MpvCommandBuilderAddsTheOptionalIpcServerBeforePlaylistUrls()
    {
        var command = MpvCommandBuilder.Build(new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions(), inputIpcServerPath: "/run/user/1000/silverscreen/mpv.sock");

        Assert.Equal("--input-ipc-server=/run/user/1000/silverscreen/mpv.sock", command.Arguments[^2]);
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
    public void PlaybackUrlAndQualityHelpersMatchTheExternalMpvContract()
    {
        var urls = MpvCommandBuilder.GetPlaybackUrls(new PlaybackRequest([
            CreateVideo("abc123_X-yZ"),
            CreateVideo("dQw4w9WgXcQ", "https://example.test/video"),
            CreateVideo("M7lc1UVf-VE", "https://youtu.be/M7lc1UVf-VE")
        ]));

        Assert.Equal([
            "https://www.youtube.com/watch?v=abc123_X-yZ",
            "https://example.test/video",
            "https://youtu.be/M7lc1UVf-VE"
        ], urls);
        Assert.Null(MpvCommandBuilder.BuildYtdlFormat("Best"));
        Assert.Equal("bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            MpvCommandBuilder.BuildYtdlFormat("1080p"));
        Assert.Equal("bestvideo[height<=720]+bestaudio/best[height<=720]", MpvCommandBuilder.BuildYtdlFormat("720p"));
        Assert.Equal("bestvideo[height<=480]+bestaudio/best[height<=480]", MpvCommandBuilder.BuildYtdlFormat("480p"));
        Assert.Equal("bestvideo[height<=360]+bestaudio/best[height<=360]", MpvCommandBuilder.BuildYtdlFormat("360p"));
    }

    [Fact]
    public void MpvCommandBuilderRejectsMissingPlaybackUrlCleanly()
    {
        var request = new PlaybackRequest([CreateVideo(string.Empty)]);

        Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, new PlaybackOptions()));
    }

    [Fact]
    public void MpvCommandBuilderRejectsAnEmptyPlaylist()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(new PlaybackRequest([]), new PlaybackOptions()));
    }

    [Fact]
    public void MpvCommandBuilderRejectsWholePlaylistWhenAnyUrlIsInvalid()
    {
        var request = new PlaybackRequest([
            CreateVideo("abc123_X-yZ"),
            CreateVideo("dQw4w9WgXcQ", "file:///tmp/video.mp4"),
            CreateVideo("M7lc1UVf-VE")
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, new PlaybackOptions()));
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

    private sealed class TrackingTelemetry : IYouTubePlaybackTelemetryService
    {
        public List<PlaybackRequest> Requests { get; } = [];
        public List<TrackingTelemetrySession> Sessions { get; } = [];

        public IYouTubePlaybackTelemetrySession Start(PlaybackRequest request)
        {
            Requests.Add(request);
            var session = new TrackingTelemetrySession();
            Sessions.Add(session);
            return session;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingTelemetrySession : IYouTubePlaybackTelemetrySession
    {
        public bool Disposed { get; private set; }
        public List<PlaybackPresenceState> States { get; } = [];

        public void UpdateState(PlaybackPresenceState state)
        {
            States.Add(state);
        }

        public void Dispose()
        {
            Disposed = true;
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

        public void Update(PlaybackRequest request, PlaybackPresenceState state)
        {
            Updates.Add((request, state));
        }
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
}