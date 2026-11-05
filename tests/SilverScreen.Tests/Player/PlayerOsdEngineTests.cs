using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public class PlayerOsdStateTests
{
    [Fact]
    public void ProcessVolume_ReturnsExpectedModel()
    {
        var state = new PlayerOsdState();

        var muted = state.ProcessVolume(80, true);
        Assert.Equal("audio-volume-muted-symbolic", muted.IconName);
        Assert.Equal("Muted", muted.Text);
        Assert.Equal(OsdActionKind.Volume, state.CurrentActionKind);

        var unmuted = state.ProcessVolume(80, false);
        Assert.Equal("audio-volume-high-symbolic", unmuted.IconName);
        Assert.Equal("80%", unmuted.Text);
    }

    [Fact]
    public void ProcessVolume_ClampsValuesCorrectly()
    {
        var state = new PlayerOsdState();

        var negative = state.ProcessVolume(-10, false);
        Assert.Equal("0%", negative.Text);
        Assert.Equal("audio-volume-muted-symbolic", negative.IconName);

        var overHundred = state.ProcessVolume(120, false);
        Assert.Equal("100%", overHundred.Text);
        Assert.Equal("audio-volume-high-symbolic", overHundred.IconName);
    }

    [Fact]
    public void ProcessSeek_AggregatesWithinWindow()
    {
        long currentTick = 1000;
        var state = new PlayerOsdState(600, () => currentTick);

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
        var state = new PlayerOsdState(600, () => currentTick);

        // Seek +10s
        state.ProcessSeek(10);
        Assert.Equal(10, state.AccumulatedSeekDeltaSeconds);

        // VolumeUp at tick = 1200
        currentTick = 1200;
        var volume = state.ProcessVolume(85, false);
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
        var state = new PlayerOsdState(600, () => currentTick);

        state.ProcessSeek(10);
        currentTick = 1200;
        var netZero = state.ProcessSeek(-10);

        Assert.Equal("0s", netZero.Text);
        Assert.Equal("media-seek-forward-symbolic", netZero.IconName);
        Assert.Equal(0, state.AccumulatedSeekDeltaSeconds);
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
}