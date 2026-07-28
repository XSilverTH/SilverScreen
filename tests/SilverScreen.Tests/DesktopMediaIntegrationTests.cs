using SilverScreen;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class DesktopMediaIntegrationTests
{
    [Fact]
    public void MprisSnapshotUsesCurrentVideoAndMicrosecondTimeUnits()
    {
        var request = new PlaybackRequest([
            new VideoSummary("firstVideo01", "First", "First channel", TimeSpan.FromSeconds(30),
                "https://example.test/first.jpg", false),
            new VideoSummary("secondVid02", "Current video", "Current channel", TimeSpan.FromMinutes(2),
                "https://example.test/current.jpg", false, "https://example.test/watch")
        ]);
        var state = new LibMpvPlaybackState(1, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), false, false,
            50, 1.5, true, true, false, []);

        var snapshot = DesktopMediaIntegration.DesktopPlaybackSnapshot.Create(request, state);

        Assert.True(snapshot.IsPlaying);
        Assert.Equal("Playing", snapshot.PlaybackStatus);
        Assert.Equal(15_000_000, snapshot.PositionMicroseconds);
        Assert.Equal(0.5, snapshot.Volume);
        Assert.Equal(1.5, snapshot.Rate);
        Assert.True(snapshot.CanSeek);
        Assert.False(snapshot.CanGoNext);
        Assert.True(snapshot.CanGoPrevious);
        Assert.Equal("Current video", snapshot.Metadata["xesam:title"].GetString());
        Assert.Equal("https://example.test/current.jpg", snapshot.Metadata["mpris:artUrl"].GetString());
        Assert.Equal("https://example.test/watch", snapshot.Metadata["xesam:url"].GetString());
        Assert.Equal(120_000_000, snapshot.Metadata["mpris:length"].GetInt64());
    }

    [Fact]
    public void MprisSnapshotStopsWhenNoMediaIsLoaded()
    {
        var state = new LibMpvPlaybackState(-1, TimeSpan.Zero, TimeSpan.Zero, true, false, 80, 1, false, false,
            false, []);

        var snapshot = DesktopMediaIntegration.DesktopPlaybackSnapshot.Create(null, state);

        Assert.False(snapshot.IsPlaying);
        Assert.Equal("Stopped", snapshot.PlaybackStatus);
        Assert.False(snapshot.CanPlay);
        Assert.Empty(snapshot.Metadata);
    }
}
