using System.Diagnostics;
using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public sealed class PlayerStreamDelayStateTests
{
    [Fact]
    public void StartActivatesDelayWithProvidedDuration()
    {
        var timestamp = 1000L;
        var state = new PlayerStreamDelayState(() => timestamp);

        state.Start(TimeSpan.FromSeconds(4.0));

        Assert.True(state.IsActive);
        Assert.Equal(TimeSpan.FromSeconds(4.0), state.TotalDelay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StartWithZeroOrNegativeDurationResetsAndRemainsInactive(int seconds)
    {
        var timestamp = 1000L;
        var state = new PlayerStreamDelayState(() => timestamp);

        state.Start(TimeSpan.FromSeconds(seconds));

        Assert.False(state.IsActive);
        Assert.Equal(TimeSpan.Zero, state.TotalDelay);
    }

    [Fact]
    public void UpdateReturnsCompleteWhenNotActive()
    {
        var state = new PlayerStreamDelayState();

        var (fraction, remainingSeconds, isComplete) = state.Update();

        Assert.Equal(1.0, fraction);
        Assert.Equal(0.0, remainingSeconds);
        Assert.True(isComplete);
    }

    [Fact]
    public void UpdateCalculatesProgressAndRemainingTimeAccurately()
    {
        var timestamp = 10_000L;
        var state = new PlayerStreamDelayState(() => timestamp);
        var total = TimeSpan.FromSeconds(4.0);

        state.Start(total);

        // At 0% elapsed
        var (f0, r0, c0) = state.Update();
        Assert.Equal(0.0, f0, 3);
        Assert.Equal(4.0, r0, 2);
        Assert.False(c0);

        // Advance 1 second (25% elapsed)
        timestamp += Stopwatch.Frequency;
        var (f1, r1, c1) = state.Update();
        Assert.Equal(0.25, f1, 3);
        Assert.Equal(3.0, r1, 2);
        Assert.False(c1);

        // Advance 2 more seconds (75% elapsed)
        timestamp += 2 * Stopwatch.Frequency;
        var (f2, r2, c2) = state.Update();
        Assert.Equal(0.75, f2, 3);
        Assert.Equal(1.0, r2, 2);
        Assert.False(c2);

        // Advance 1 more second (100% elapsed)
        timestamp += Stopwatch.Frequency;
        var (f3, r3, c3) = state.Update();
        Assert.Equal(1.0, f3, 3);
        Assert.Equal(0.0, r3, 2);
        Assert.True(c3);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void UpdateMarksCompleteWhenElapsedExceedsTotalDuration()
    {
        var timestamp = 1_000L;
        var state = new PlayerStreamDelayState(() => timestamp);

        state.Start(TimeSpan.FromSeconds(2.0));

        // Advance 5 seconds (well beyond 2 seconds)
        timestamp += 5 * Stopwatch.Frequency;
        var (fraction, remainingSeconds, isComplete) = state.Update();

        Assert.Equal(1.0, fraction);
        Assert.Equal(0.0, remainingSeconds);
        Assert.True(isComplete);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void ResetClearsActiveStateAndDelay()
    {
        var state = new PlayerStreamDelayState();
        state.Start(TimeSpan.FromSeconds(5.0));
        Assert.True(state.IsActive);

        state.Reset();

        Assert.False(state.IsActive);
        Assert.Equal(TimeSpan.Zero, state.TotalDelay);
    }

    [Fact]
    public void FormatLabelFormatsCultureInvariantlyWithExpectedCopy()
    {
        var label = PlayerStreamDelayState.FormatLabel(3.456);
        Assert.Equal("YouTube ad-block delay… 3.5s", label);

        var labelZero = PlayerStreamDelayState.FormatLabel(0.0);
        Assert.Equal("YouTube ad-block delay… 0.0s", labelZero);
    }

    [Theory]
    // Light colors (should return true -> black text)
    [InlineData(0.745, 0.761, 1.0, true)]   // Noctalia lavender #bec2ff
    [InlineData(1.0, 1.0, 1.0, true)]       // Pure white
    [InlineData(0.965, 0.827, 0.176, true)] // Yellow #f6d32d
    [InlineData(0.976, 0.753, 0.922, true)] // Light pink
    [InlineData(0.561, 0.941, 0.643, true)] // Mint green
    // Dark colors (should return false -> white text)
    [InlineData(0.0, 0.0, 0.0, false)]      // Pure black
    [InlineData(0.09, 0.09, 0.09, false)]   // Trough dark gray
    [InlineData(0.208, 0.518, 0.894, false)]// Adwaita blue #3584e4
    [InlineData(0.753, 0.110, 0.157, false)]// Adwaita red #c01c28
    [InlineData(0.149, 0.635, 0.412, false)]// Adwaita green #26a269
    [InlineData(0.569, 0.255, 0.675, false)]// Adwaita purple #9141ac
    public void IsLightColor_CorrectlyDifferentiatesLightAndDarkColors(double r, double g, double b, bool expectedIsLight)
    {
        var result = PlayerStreamDelayController.IsLightColor(r, g, b);
        Assert.Equal(expectedIsLight, result);
    }
}
