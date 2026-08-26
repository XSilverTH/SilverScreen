using SilverScreen.Core.Player;

namespace SilverScreen.Tests.Player;

public sealed class PlaybackStatsFormatterTests
{
    [Theory]
    [InlineData(0.0, "—")]
    [InlineData(-100.0, "—")]
    [InlineData(500.0, "500 bps")]
    [InlineData(128_000.0, "128 kbps")]
    [InlineData(4_500_000.0, "4.50 Mbps")]
    [InlineData(1_200_000_000.0, "1.20 Gbps")]
    public void FormatBitrate_ReturnsExpectedString(double? input, string expected)
    {
        var result = PlaybackStatsFormatter.FormatBitrate(input);
        Assert.Equal(expected, result);
    }
    [Theory]
    [InlineData(0L, "—")]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(10485760L, "10.0 MB")]
    [InlineData(2147483648L, "2.00 GB")]
    public void FormatBytes_ReturnsExpectedString(long? input, string expected)
    {
        var result = PlaybackStatsFormatter.FormatBytes(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatFps_HandlesAllCombinations()
    {
        Assert.Equal("—", PlaybackStatsFormatter.FormatFps(null, null));
        Assert.Equal("60.00 fps", PlaybackStatsFormatter.FormatFps(60, null));
        Assert.Equal("59.98 fps (estimated)", PlaybackStatsFormatter.FormatFps(null, 59.98));
        Assert.Equal("60.00 fps (container) / 59.98 fps (estimated)", PlaybackStatsFormatter.FormatFps(60, 59.98));
    }

    [Fact]
    public void FormatResolution_HandlesNativeAndScaled()
    {
        Assert.Equal("—", PlaybackStatsFormatter.FormatResolution(null, null, null, null, null));
        Assert.Equal("1920×1080", PlaybackStatsFormatter.FormatResolution(1920, 1080, null, null, null));
        Assert.Equal("1920×1080 (Aspect: 1.78)", PlaybackStatsFormatter.FormatResolution(1920, 1080, null, null, 1.777778));
        Assert.Equal("1920×1080 (Aspect: 1.78) → 1280×720", PlaybackStatsFormatter.FormatResolution(1920, 1080, 1280, 720, 1.777778));
    }

    [Fact]
    public void FormatAvSync_FormatsMillisecondsWithSign()
    {
        Assert.Equal("—", PlaybackStatsFormatter.FormatAvSync(null));
        Assert.Equal("+0.00 ms", PlaybackStatsFormatter.FormatAvSync(0));
        Assert.Equal("+2.50 ms", PlaybackStatsFormatter.FormatAvSync(0.0025));
        Assert.Equal("-1.20 ms", PlaybackStatsFormatter.FormatAvSync(-0.0012));
    }

    [Fact]
    public void FormatDroppedFrames_FormatsTotalVoAndMistimed()
    {
        Assert.Equal("0 (VO: 0, Mistimed: 0)", PlaybackStatsFormatter.FormatDroppedFrames(null, null, null));
        Assert.Equal("5 (VO: 2, Mistimed: 1)", PlaybackStatsFormatter.FormatDroppedFrames(5, 2, 1));
    }

    [Fact]
    public void FormatCache_HandlesSecondsAndBytes()
    {
        Assert.Equal("—", PlaybackStatsFormatter.FormatCache(null, null));
        Assert.Equal("15.4 s", PlaybackStatsFormatter.FormatCache(15.4, null));
        Assert.Equal("20.0 MB", PlaybackStatsFormatter.FormatCache(null, 20 * 1024 * 1024));
        Assert.Equal("15.4 s (20.0 MB)", PlaybackStatsFormatter.FormatCache(15.4, 20 * 1024 * 1024));
    }

    [Fact]
    public void FormatTime_FormatsTimeSpanAndPercent()
    {
        Assert.Equal("1:30 / 3:00", PlaybackStatsFormatter.FormatTime(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(180), null));
        Assert.Equal("1:30 / 3:00 (50.0%)", PlaybackStatsFormatter.FormatTime(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(180), 50.0));
        Assert.Equal("1:00:00 / 2:00:00 (50.0%)", PlaybackStatsFormatter.FormatTime(TimeSpan.FromHours(1), TimeSpan.FromHours(2), 50.0));
    }

    [Fact]
    public void FormatOverviewPageMarkup_ContainsEssentialSections()
    {
        var stats = CreateSampleStats();
        var markup = PlaybackStatsFormatter.FormatOverviewPageMarkup(stats);

        Assert.Contains("[1/4] Overview", markup);
        Assert.Contains("FILE &amp; STREAM", markup);
        Assert.Contains("Sample Video Title", markup);
        Assert.Contains("VIDEO STREAM", markup);
        Assert.Contains("av01.0.08M.08", markup);
        Assert.Contains("vaapi (hw)", markup);
        Assert.Contains("1920×1080", markup);
        Assert.Contains("AUDIO STREAM", markup);
        Assert.Contains("Opus", markup);
        Assert.Contains("PERFORMANCE &amp; BUFFERING", markup);
        Assert.Contains("PLAYBACK", markup);
    }

    [Fact]
    public void FormatOverviewPageMarkup_UsesCustomAccentColor()
    {
        var stats = CreateSampleStats();
        var markup = PlaybackStatsFormatter.FormatOverviewPageMarkup(stats, "#e66100");

        Assert.Contains("foreground=\"#e66100\"", markup);
        Assert.Contains("<span weight=\"bold\" foreground=\"#e66100\"><b>[1/4] Overview</b></span>", markup);
        Assert.Contains("<span weight=\"bold\" foreground=\"#e66100\"><b>FILE &amp; STREAM</b></span>", markup);
    }

    [Fact]
    public void FormatPerformancePageMarkup_ContainsDetailedPerformanceData()
    {
        var stats = CreateSampleStats();
        var markup = PlaybackStatsFormatter.FormatPerformancePageMarkup(stats);

        Assert.Contains("[2/4] Performance", markup);
        Assert.Contains("FRAME &amp; RENDERING PERFORMANCE", markup);
        Assert.Contains("59.98 fps", markup);
        Assert.Contains("BITRATE &amp; BANDWIDTH", markup);
        Assert.Contains("CACHE &amp; DEMUXER BUFFER", markup);
    }

    [Fact]
    public void FormatTracksPageMarkup_ListsAllTrackTypes()
    {
        var stats = CreateSampleStats();
        var markup = PlaybackStatsFormatter.FormatTracksPageMarkup(stats);

        Assert.Contains("[3/4] Tracks", markup);
        Assert.Contains("VIDEO TRACKS", markup);
        Assert.Contains("AUDIO TRACKS", markup);
        Assert.Contains("SUBTITLE TRACKS", markup);
        Assert.Contains("English [cc]", markup);
    }

    [Fact]
    public void FormatEnginePageMarkup_ContainsEngineAndEnvironmentInfo()
    {
        var stats = CreateSampleStats();
        var markup = PlaybackStatsFormatter.FormatEnginePageMarkup(stats);

        Assert.Contains("[4/4] Engine", markup);
        Assert.Contains("MEDIA BACKEND", markup);
        Assert.Contains("libmpv", markup);
        Assert.Contains("mpv 0.38.0", markup);
        Assert.Contains("APPLICATION", markup);
        Assert.Contains("SilverScreen", markup);
    }

    [Fact]
    public void FormatFullSummary_GeneratesComprehensivePlainTextReport()
    {
        var stats = CreateSampleStats();
        var summary = PlaybackStatsFormatter.FormatFullSummary(stats);

        Assert.Contains("=== SILVERSCREEN PLAYBACK STATISTICS ===", summary);
        Assert.Contains("[FILE & STREAM]", summary);
        Assert.Contains("Title: Sample Video Title", summary);
        Assert.Contains("[VIDEO STREAM]", summary);
        Assert.Contains("[AUDIO STREAM]", summary);
        Assert.Contains("[PERFORMANCE & BUFFERING]", summary);
        Assert.Contains("[PLAYBACK]", summary);
        Assert.Contains("[TRACKS]", summary);
        Assert.Contains("[ENGINE]", summary);
    }

    private static PlaybackStats CreateSampleStats()
    {
        return new PlaybackStats(
            Title: "Sample Video Title",
            FileFormat: "matroska,webm",
            Demuxer: "lavf",
            ProtocolOrUrl: "https://example.com/video.webm",
            FileSize: 150 * 1024 * 1024,
            VideoCodec: "av01.0.08M.08",
            VideoDecoder: "libdav1d",
            HwDec: "vaapi",
            VideoWidth: 1920,
            VideoHeight: 1080,
            DisplayWidth: 1920,
            DisplayHeight: 1080,
            AspectRatio: 1.777778,
            ContainerFps: 60.0,
            EstimatedFps: 59.98,
            VideoBitrate: 4_250_000,
            PixelFormat: "yuv420p",
            ColorMatrix: "bt709",
            ColorLevels: "limited",
            Primaries: "bt709",
            Gamma: "bt709",
            AudioCodec: "Opus",
            AudioDecoder: "opus",
            AudioSampleRate: 48000,
            AudioChannels: 2,
            AudioChannelLayout: "stereo",
            AudioFormat: "fltp",
            AudioBitrate: 160_000,
            DroppedFrames: 0,
            VoDroppedFrames: 0,
            MistimedFrames: 0,
            VsyncRatio: 1.0,
            AvSyncDifference: 0.001,
            CacheDuration: 25.5,
            CacheBytes: 32 * 1024 * 1024,
            Position: TimeSpan.FromMinutes(5),
            Duration: TimeSpan.FromMinutes(20),
            PercentPosition: 25.0,
            Speed: 1.0,
            Volume: 100,
            IsMuted: false,
            IsPaused: false,
            SubtitleTrack: "English [cc]",
            AudioTrack: "English",
            Tracks:
            [
                new PlaybackStatsTrack(1, "video", null, null, "av01", 1920, 1080, 60.0, null, null, 4_250_000, true, true),
                new PlaybackStatsTrack(2, "audio", "English", "en", "opus", null, null, null, 48000, "stereo", 160_000, true, true),
                new PlaybackStatsTrack(3, "sub", "English [cc]", "en", "vtt", null, null, null, null, null, null, true, false)
            ],
            MpvVersion: "mpv 0.38.0",
            FfmpegVersion: "7.1",
            VoBackend: "libmpv (OpenGL)");
    }
}
