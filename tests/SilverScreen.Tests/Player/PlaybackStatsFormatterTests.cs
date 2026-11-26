using System.Runtime.InteropServices;
using SilverScreen.Core.Player;

namespace SilverScreen.Tests.Player;

public sealed class PlaybackStatsFormatterTests
{
    [Fact]
    public void FormatTracksPageMarkup_WithManySubtitles_CapsOutputAndKeepsSelectedTrack()
    {
        var tracks = new List<PlaybackStatsTrack>();
        for (var i = 1; i <= 50; i++)
        {
            var isSelected = i == 45;
            tracks.Add(new PlaybackStatsTrack(
                i,
                "sub",
                $"Subtitle Track {i}",
                $"lang_{i}",
                "webvtt",
                null,
                null,
                null,
                null,
                null,
                null,
                isSelected,
                i == 1));
        }

        var stats = new PlaybackStats(
            "Test",
            "mkv",
            "lavf",
            "https://example.com",
            1000,
            "av1",
            null,
            null,
            1920,
            1080,
            1920,
            1080,
            1.77,
            60,
            60,
            2000000,
            "yuv420p",
            null,
            null,
            null,
            null,
            "opus",
            null,
            48000,
            2,
            "stereo",
            null,
            128000,
            0,
            0,
            0,
            1,
            0,
            10,
            1024,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(5),
            0,
            1,
            100,
            false,
            false,
            null,
            null,
            tracks,
            "mpv",
            "ffmpeg",
            "opengl");

        var markup = PlaybackStatsFormatter.FormatTracksPageMarkup(stats);

        // Verify it contains the cap indicator
        Assert.Contains("more subtitle tracks", markup);
        // Verify the selected track (#45) is included in the output even though it's beyond the first 10
        Assert.Contains("[#45]", markup);
        Assert.Contains("[Selected]", markup);
    }

    [Fact]
    public void FormatFullSummary_WithManyTracks_CapsOutput()
    {
        var tracks = new List<PlaybackStatsTrack>();
        for (var i = 1; i <= 60; i++)
            tracks.Add(new PlaybackStatsTrack(
                i,
                "sub",
                $"Track {i}",
                "en",
                "webvtt",
                null,
                null,
                null,
                null,
                null,
                null,
                i == 55,
                false));

        var stats = new PlaybackStats(
            "Test",
            "mkv",
            "lavf",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            1,
            100,
            false,
            false,
            null,
            null,
            tracks,
            null,
            null,
            null);

        var summary = PlaybackStatsFormatter.FormatFullSummary(stats);
        Assert.Contains("... and ", summary);
        Assert.Contains("#55", summary);
    }

    [Fact]
    public void FormatEnginePageMarkup_IncludesCurrentRuntimeFrameworkDescription()
    {
        var stats = new PlaybackStats(
            "Test",
            "mkv",
            "lavf",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            1,
            100,
            false,
            false,
            null,
            null,
            [],
            null,
            null,
            null);

        var markup = PlaybackStatsFormatter.FormatEnginePageMarkup(stats);

        Assert.Contains(RuntimeInformation.FrameworkDescription, markup);
    }
}