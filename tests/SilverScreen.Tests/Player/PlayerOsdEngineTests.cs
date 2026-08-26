using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public class PlayerOsdEngineTests
{
    [Fact]
    public void FormatSeekDelta_FormatsPositiveNegativeAndZeroOffsets()
    {
        Assert.Equal("0s", PlayerOsdEngine.FormatSeekDelta(0));
        Assert.Equal("+10s", PlayerOsdEngine.FormatSeekDelta(10));
        Assert.Equal("+20s", PlayerOsdEngine.FormatSeekDelta(20));
        Assert.Equal("-10s", PlayerOsdEngine.FormatSeekDelta(-10));
        Assert.Equal("-30s", PlayerOsdEngine.FormatSeekDelta(-30));
        Assert.Equal("+1m", PlayerOsdEngine.FormatSeekDelta(60));
        Assert.Equal("+1m 10s", PlayerOsdEngine.FormatSeekDelta(70));
        Assert.Equal("-1m", PlayerOsdEngine.FormatSeekDelta(-60));
        Assert.Equal("-2m 30s", PlayerOsdEngine.FormatSeekDelta(-150));
    }

    [Fact]
    public void FormatSpeed_FormatsDecimalRatesCorrectly()
    {
        Assert.Equal("1×", PlayerOsdEngine.FormatSpeed(1.0));
        Assert.Equal("1.25×", PlayerOsdEngine.FormatSpeed(1.25));
        Assert.Equal("1.5×", PlayerOsdEngine.FormatSpeed(1.5));
        Assert.Equal("2×", PlayerOsdEngine.FormatSpeed(2.0));
        Assert.Equal("0.25×", PlayerOsdEngine.FormatSpeed(0.25));
    }

    [Fact]
    public void GetVolumeIcon_ResolvesAppropriateSymbolicIcons()
    {
        Assert.Equal("audio-volume-muted-symbolic", PlayerOsdEngine.GetVolumeIcon(80, isMuted: true));
        Assert.Equal("audio-volume-muted-symbolic", PlayerOsdEngine.GetVolumeIcon(0, isMuted: false));
        Assert.Equal("audio-volume-low-symbolic", PlayerOsdEngine.GetVolumeIcon(20, isMuted: false));
        Assert.Equal("audio-volume-low-symbolic", PlayerOsdEngine.GetVolumeIcon(33, isMuted: false));
        Assert.Equal("audio-volume-medium-symbolic", PlayerOsdEngine.GetVolumeIcon(34, isMuted: false));
        Assert.Equal("audio-volume-medium-symbolic", PlayerOsdEngine.GetVolumeIcon(66, isMuted: false));
        Assert.Equal("audio-volume-high-symbolic", PlayerOsdEngine.GetVolumeIcon(67, isMuted: false));
        Assert.Equal("audio-volume-high-symbolic", PlayerOsdEngine.GetVolumeIcon(100, isMuted: false));
    }

    [Fact]
    public void ProcessPlayPause_ReturnsExpectedModel()
    {
        var engine = new PlayerOsdEngine();

        var paused = engine.ProcessPlayPause(true);
        Assert.Equal("media-playback-pause-symbolic", paused.IconName);
        Assert.Equal("Paused", paused.Text);
        Assert.Equal(OsdActionKind.PlayPause, engine.CurrentActionKind);

        var playing = engine.ProcessPlayPause(false);
        Assert.Equal("media-playback-start-symbolic", playing.IconName);
        Assert.Equal("Playing", playing.Text);
    }

    [Fact]
    public void ProcessVolume_ReturnsExpectedModel()
    {
        var engine = new PlayerOsdEngine();

        var muted = engine.ProcessVolume(80, isMuted: true);
        Assert.Equal("audio-volume-muted-symbolic", muted.IconName);
        Assert.Equal("Muted", muted.Text);
        Assert.Equal(OsdActionKind.Volume, engine.CurrentActionKind);

        var unmuted = engine.ProcessVolume(80, isMuted: false);
        Assert.Equal("audio-volume-high-symbolic", unmuted.IconName);
        Assert.Equal("80%", unmuted.Text);
    }

    [Fact]
    public void ProcessVolume_ClampsValuesCorrectly()
    {
        var engine = new PlayerOsdEngine();

        var negative = engine.ProcessVolume(-10, isMuted: false);
        Assert.Equal("0%", negative.Text);
        Assert.Equal("audio-volume-muted-symbolic", negative.IconName);

        var overHundred = engine.ProcessVolume(120, isMuted: false);
        Assert.Equal("100%", overHundred.Text);
        Assert.Equal("audio-volume-high-symbolic", overHundred.IconName);
    }

    [Fact]
    public void ProcessSpeed_ReturnsExpectedModel()
    {
        var engine = new PlayerOsdEngine();

        var speed = engine.ProcessSpeed(1.25);
        Assert.Equal("speedometer-symbolic", speed.IconName);
        Assert.Equal("1.25×", speed.Text);
        Assert.Equal(OsdActionKind.Speed, engine.CurrentActionKind);
    }

    [Fact]
    public void ProcessSeek_AggregatesWithinWindow()
    {
        long currentTick = 1000;
        var engine = new PlayerOsdEngine(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        // First seek +10s
        var first = engine.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", first.IconName);
        Assert.Equal("+10s", first.Text);
        Assert.Equal(10, engine.AccumulatedSeekDeltaSeconds);

        // Second seek +10s within 300ms (tick = 1300) -> aggregates to +20s
        currentTick = 1300;
        var second = engine.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", second.IconName);
        Assert.Equal("+20s", second.Text);
        Assert.Equal(20, engine.AccumulatedSeekDeltaSeconds);

        // Third seek -30s within 200ms (tick = 1500) -> aggregates to -10s
        currentTick = 1500;
        var third = engine.ProcessSeek(-30);
        Assert.Equal("media-seek-backward-symbolic", third.IconName);
        Assert.Equal("-10s", third.Text);
        Assert.Equal(-10, engine.AccumulatedSeekDeltaSeconds);

        // Fourth seek after window expires (tick = 2200, elapsed = 700ms > 600ms) -> starts new window
        currentTick = 2200;
        var fourth = engine.ProcessSeek(10);
        Assert.Equal("media-seek-forward-symbolic", fourth.IconName);
        Assert.Equal("+10s", fourth.Text);
        Assert.Equal(10, engine.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ActionSwitch_ResetsSeekAggregation()
    {
        long currentTick = 1000;
        var engine = new PlayerOsdEngine(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        // Seek +10s
        engine.ProcessSeek(10);
        Assert.Equal(10, engine.AccumulatedSeekDeltaSeconds);

        // VolumeUp at tick = 1200
        currentTick = 1200;
        var volume = engine.ProcessVolume(85, isMuted: false);
        Assert.Equal(OsdActionKind.Volume, engine.CurrentActionKind);
        Assert.Equal("85%", volume.Text);
        Assert.Equal(0, engine.AccumulatedSeekDeltaSeconds);

        // Seek again at tick = 1300 -> starts fresh seek
        currentTick = 1300;
        var seek = engine.ProcessSeek(10);
        Assert.Equal("+10s", seek.Text);
        Assert.Equal(10, engine.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessSeek_MixedRapidDeltas_AccumulateToZero()
    {
        long currentTick = 1000;
        var engine = new PlayerOsdEngine(aggregationWindowMilliseconds: 600, tickCountProvider: () => currentTick);

        engine.ProcessSeek(10);
        currentTick = 1200;
        var netZero = engine.ProcessSeek(-10);

        Assert.Equal("0s", netZero.Text);
        Assert.Equal("media-seek-forward-symbolic", netZero.IconName);
        Assert.Equal(0, engine.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessStats_ReturnsExpectedModel()
    {
        var engine = new PlayerOsdEngine();

        var open = engine.ProcessStats(true);
        Assert.Equal("utilities-system-monitor-symbolic", open.IconName);
        Assert.Equal("Playback Stats: Open", open.Text);
        Assert.Equal(OsdActionKind.Stats, engine.CurrentActionKind);

        var closed = engine.ProcessStats(false);
        Assert.Equal("utilities-system-monitor-symbolic", closed.IconName);
        Assert.Equal("Playback Stats: Closed", closed.Text);
    }

    [Fact]
    public void Reset_ClearsCurrentActionAndAccumulatedState()
    {
        var engine = new PlayerOsdEngine();
        engine.ProcessSeek(30);
        Assert.True(engine.IsActive);
        Assert.Equal(OsdActionKind.Seek, engine.CurrentActionKind);
        Assert.Equal(30, engine.AccumulatedSeekDeltaSeconds);

        engine.Reset();
        Assert.False(engine.IsActive);
        Assert.Equal(OsdActionKind.None, engine.CurrentActionKind);
        Assert.Equal(0, engine.AccumulatedSeekDeltaSeconds);
    }

    [Fact]
    public void ProcessAllActionKinds_ProduceAccurateOutputs()
    {
        var engine = new PlayerOsdEngine();

        Assert.Equal("Beginning", engine.ProcessSeekToBeginning().Text);
        Assert.Equal("English", engine.ProcessSubtitles("English").Text);
        Assert.Equal("Off", engine.ProcessSubtitles(string.Empty).Text);
        Assert.Equal("Queue Open", engine.ProcessQueue(true).Text);
        Assert.Equal("Queue Closed", engine.ProcessQueue(false).Text);
        Assert.Equal("Video Info Open", engine.ProcessVideoInfo(true).Text);
        Assert.Equal("Video Info Closed", engine.ProcessVideoInfo(false).Text);
        Assert.Equal("Fullscreen", engine.ProcessFullscreen(true).Text);
        Assert.Equal("Exit Fullscreen", engine.ProcessFullscreen(false).Text);
        Assert.Equal("Next Video", engine.ProcessNextVideo().Text);
        Assert.Equal("Previous Video", engine.ProcessPreviousVideo().Text);
        Assert.Equal("Resumed", engine.ProcessResumed().Text);
        Assert.Equal("Skipped Sponsor", engine.ProcessSkippedSponsor().Text);
    }
}
