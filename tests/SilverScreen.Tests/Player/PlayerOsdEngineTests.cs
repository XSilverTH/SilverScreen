using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public class PlayerOsdStateTests
{
    [Fact]
    public void FormatSeekDelta_FormatsPositiveNegativeAndZeroOffsets()
    {
        Assert.Equal("0s", PlayerOsdState.FormatSeekDelta(0));
        Assert.Equal("+10s", PlayerOsdState.FormatSeekDelta(10));
        Assert.Equal("+20s", PlayerOsdState.FormatSeekDelta(20));
        Assert.Equal("-10s", PlayerOsdState.FormatSeekDelta(-10));
        Assert.Equal("-30s", PlayerOsdState.FormatSeekDelta(-30));
        Assert.Equal("+1m", PlayerOsdState.FormatSeekDelta(60));
        Assert.Equal("+1m 10s", PlayerOsdState.FormatSeekDelta(70));
        Assert.Equal("-1m", PlayerOsdState.FormatSeekDelta(-60));
        Assert.Equal("-2m 30s", PlayerOsdState.FormatSeekDelta(-150));
    }

    [Fact]
    public void FormatSpeed_FormatsDecimalRatesCorrectly()
    {
        Assert.Equal("1×", PlayerOsdState.FormatSpeed(1.0));
        Assert.Equal("1.25×", PlayerOsdState.FormatSpeed(1.25));
        Assert.Equal("1.5×", PlayerOsdState.FormatSpeed(1.5));
        Assert.Equal("2×", PlayerOsdState.FormatSpeed(2.0));
        Assert.Equal("0.25×", PlayerOsdState.FormatSpeed(0.25));
    }

    [Fact]
    public void GetVolumeIcon_ResolvesAppropriateSymbolicIcons()
    {
        Assert.Equal("audio-volume-muted-symbolic", PlayerOsdState.GetVolumeIcon(80, isMuted: true));
        Assert.Equal("audio-volume-muted-symbolic", PlayerOsdState.GetVolumeIcon(0, isMuted: false));
        Assert.Equal("audio-volume-low-symbolic", PlayerOsdState.GetVolumeIcon(20, isMuted: false));
        Assert.Equal("audio-volume-low-symbolic", PlayerOsdState.GetVolumeIcon(33, isMuted: false));
        Assert.Equal("audio-volume-medium-symbolic", PlayerOsdState.GetVolumeIcon(34, isMuted: false));
        Assert.Equal("audio-volume-medium-symbolic", PlayerOsdState.GetVolumeIcon(66, isMuted: false));
        Assert.Equal("audio-volume-high-symbolic", PlayerOsdState.GetVolumeIcon(67, isMuted: false));
        Assert.Equal("audio-volume-high-symbolic", PlayerOsdState.GetVolumeIcon(100, isMuted: false));
    }

    [Fact]
    public void ProcessPlayPause_ReturnsExpectedModel()
    {
        var state = new PlayerOsdState();

        var paused = state.ProcessPlayPause(true);
        Assert.Equal("media-playback-pause-symbolic", paused.IconName);
        Assert.Equal("Paused", paused.Text);
        Assert.Equal(OsdActionKind.PlayPause, state.CurrentActionKind);

        var playing = state.ProcessPlayPause(false);
        Assert.Equal("media-playback-start-symbolic", playing.IconName);
        Assert.Equal("Playing", playing.Text);
    }

    [Fact]
    public void ProcessVolume_ReturnsExpectedModel()
    {
        var state = new PlayerOsdState();

        var muted = state.ProcessVolume(80, isMuted: true);
        Assert.Equal("audio-volume-muted-symbolic", muted.IconName);
        Assert.Equal("Muted", muted.Text);
        Assert.Equal(OsdActionKind.Volume, state.CurrentActionKind);

        var unmuted = state.ProcessVolume(80, isMuted: false);
        Assert.Equal("audio-volume-high-symbolic", unmuted.IconName);
        Assert.Equal("80%", unmuted.Text);
    }

    [Fact]
    public void ProcessVolume_ClampsValuesCorrectly()
    {
        var state = new PlayerOsdState();

        var negative = state.ProcessVolume(-10, isMuted: false);
        Assert.Equal("0%", negative.Text);
        Assert.Equal("audio-volume-muted-symbolic", negative.IconName);

        var overHundred = state.ProcessVolume(120, isMuted: false);
        Assert.Equal("100%", overHundred.Text);
        Assert.Equal("audio-volume-high-symbolic", overHundred.IconName);
    }

    [Fact]
    public void ProcessSpeed_ReturnsExpectedModel()
    {
        var state = new PlayerOsdState();

        var speed = state.ProcessSpeed(1.25);
        Assert.Equal("speedometer-symbolic", speed.IconName);
        Assert.Equal("1.25×", speed.Text);
        Assert.Equal(OsdActionKind.Speed, state.CurrentActionKind);
    }

    [Fact]
    public void ProcessSeek_AggregatesWithinWindow()
    {
        long currentTick = 1000;
        var state = new PlayerOsdState(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        // First seek +10s
        var first = state.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", first.IconName);
        Assert.Equal("+10s", first.Text);
        Assert.Equal(10, state.AccumulatedSeekDeltaSeconds);

        // Second seek +10s within 300ms (tick = 1300) -> aggregates to +20s
        currentTick = 1300;
        var second = state.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", second.IconName);
        Assert.Equal("+20s", second.Text);
        Assert.Equal(20, state.AccumulatedSeekDeltaSeconds);

        // Third seek -30s within 200ms (tick = 1500) -> aggregates to -10s
        currentTick = 1500;
        var third = state.ProcessSeek(-30);
        Assert.Equal("media-seek-backward-symbolic", third.IconName);
        Assert.Equal("-10s", third.Text);
        Assert.Equal(-10, state.AccumulatedSeekDeltaSeconds);

        // Fourth seek after window expires (tick = 2200, elapsed = 700ms > 600ms) -> starts new window
        currentTick = 2200;
        var fourth = state.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", fourth.IconName);
        Assert.Equal("+10s", fourth.Text);
        Assert.Equal(10, state.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ActionSwitch_ResetsSeekAggregation()
    {
        long currentTick = 1000;
        var state = new PlayerOsdState(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        // Seek +10s
        state.ProcessSeek(10);
        Assert.Equal(10, state.AccumulatedSeekDeltaSeconds);

        // VolumeUp at tick = 1200
        currentTick = 1200;
        var volume = state.ProcessVolume(85, isMuted: false);
        Assert.Equal(OsdActionKind.Volume, state.CurrentActionKind);
        Assert.Equal("85%", volume.Text);
        Assert.Equal(0, state.AccumulatedSeekDeltaSeconds);

        // Seek again at tick = 1300 -> starts fresh seek
        currentTick = 1300;
        var seek = state.ProcessSeek(10);
        Assert.Equal("+10s", seek.Text);
        Assert.Equal(10, state.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessSeek_MixedRapidDeltas_AccumulateToZero()
    {
        long currentTick = 1000;
        var state = new PlayerOsdState(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        state.ProcessSeek(10);
        currentTick = 1200;
        var netZero = state.ProcessSeek(-10);

        Assert.Equal("0s", netZero.Text);
        Assert.Equal("media-seek-forward-symbolic", netZero.IconName);
        Assert.Equal(0, state.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessStats_ReturnsExpectedModel()
    {
        var state = new PlayerOsdState();

        var open = state.ProcessStats(true);
        Assert.Equal("utilities-system-monitor-symbolic", open.IconName);
        Assert.Equal("Playback Stats: Open", open.Text);
        Assert.Equal(OsdActionKind.Stats, state.CurrentActionKind);

        var closed = state.ProcessStats(false);
        Assert.Equal("utilities-system-monitor-symbolic", closed.IconName);
        Assert.Equal("Playback Stats: Closed", closed.Text);
    }

    [Fact]
    public void Reset_ClearsCurrentActionAndAccumulatedState()
    {
        var state = new PlayerOsdState();
        state.ProcessSeek(30);
        Assert.True(state.IsActive);
        Assert.Equal(OsdActionKind.Seek, state.CurrentActionKind);
        Assert.Equal(30, state.AccumulatedSeekDeltaSeconds);

        state.Reset();
        Assert.False(state.IsActive);
        Assert.Equal(OsdActionKind.None, state.CurrentActionKind);
        Assert.Equal(0, state.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessAllActionKinds_ProduceAccurateOutputs()
    {
        var state = new PlayerOsdState();

        Assert.Equal("Beginning", state.ProcessSeekToBeginning().Text);
        Assert.Equal("English", state.ProcessSubtitles("English").Text);
        Assert.Equal("Off", state.ProcessSubtitles(string.Empty).Text);
        Assert.Equal("Queue Open", state.ProcessQueue(true).Text);
        Assert.Equal("Queue Closed", state.ProcessQueue(false).Text);
        Assert.Equal("Video Info Open", state.ProcessVideoInfo(true).Text);
        Assert.Equal("Video Info Closed", state.ProcessVideoInfo(false).Text);
        Assert.Equal("Fullscreen", state.ProcessFullscreen(true).Text);
        Assert.Equal("Exit Fullscreen", state.ProcessFullscreen(false).Text);
        Assert.Equal("Next Video", state.ProcessNextVideo().Text);
        Assert.Equal("Previous Video", state.ProcessPreviousVideo().Text);
        Assert.Equal("Resumed", state.ProcessResumed().Text);
        Assert.Equal("Skipped Sponsor", state.ProcessSkippedSponsor().Text);
    }
}
