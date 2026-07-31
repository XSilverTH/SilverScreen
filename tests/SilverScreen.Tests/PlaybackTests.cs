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
}