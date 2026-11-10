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
                Id: i,
                Type: "sub",
                Title: $"Subtitle Track {i}",
                Language: $"lang_{i}",
                Codec: "webvtt",
                Width: null,
                Height: null,
                Fps: null,
                SampleRate: null,
                Channels: null,
                Bitrate: null,
                IsSelected: isSelected,
                IsDefault: i == 1));
        }

        var stats = new PlaybackStats(
            Title: "Test",
            FileFormat: "mkv",
            Demuxer: "lavf",
            ProtocolOrUrl: "https://example.com",
            FileSize: 1000,
            VideoCodec: "av1",
            VideoDecoder: null,
            HwDec: null,
            VideoWidth: 1920,
            VideoHeight: 1080,
            DisplayWidth: 1920,
            DisplayHeight: 1080,
            AspectRatio: 1.77,
            ContainerFps: 60,
            EstimatedFps: 60,
            VideoBitrate: 2000000,
            PixelFormat: "yuv420p",
            ColorMatrix: null,
            ColorLevels: null,
            Primaries: null,
            Gamma: null,
            AudioCodec: "opus",
            AudioDecoder: null,
            AudioSampleRate: 48000,
            AudioChannels: 2,
            AudioChannelLayout: "stereo",
            AudioFormat: null,
            AudioBitrate: 128000,
            DroppedFrames: 0,
            VoDroppedFrames: 0,
            MistimedFrames: 0,
            VsyncRatio: 1,
            AvSyncDifference: 0,
            CacheDuration: 10,
            CacheBytes: 1024,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.FromMinutes(5),
            PercentPosition: 0,
            Speed: 1,
            Volume: 100,
            IsMuted: false,
            IsPaused: false,
            SubtitleTrack: null,
            AudioTrack: null,
            Tracks: tracks,
            MpvVersion: "mpv",
            FfmpegVersion: "ffmpeg",
            VoBackend: "opengl");

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
        {
            tracks.Add(new PlaybackStatsTrack(
                Id: i,
                Type: "sub",
                Title: $"Track {i}",
                Language: "en",
                Codec: "webvtt",
                Width: null,
                Height: null,
                Fps: null,
                SampleRate: null,
                Channels: null,
                Bitrate: null,
                IsSelected: i == 55,
                IsDefault: false));
        }

        var stats = new PlaybackStats(
            Title: "Test",
            FileFormat: "mkv",
            Demuxer: "lavf",
            ProtocolOrUrl: null,
            FileSize: null,
            VideoCodec: null,
            VideoDecoder: null,
            HwDec: null,
            VideoWidth: null,
            VideoHeight: null,
            DisplayWidth: null,
            DisplayHeight: null,
            AspectRatio: null,
            ContainerFps: null,
            EstimatedFps: null,
            VideoBitrate: null,
            PixelFormat: null,
            ColorMatrix: null,
            ColorLevels: null,
            Primaries: null,
            Gamma: null,
            AudioCodec: null,
            AudioDecoder: null,
            AudioSampleRate: null,
            AudioChannels: null,
            AudioChannelLayout: null,
            AudioFormat: null,
            AudioBitrate: null,
            DroppedFrames: null,
            VoDroppedFrames: null,
            MistimedFrames: null,
            VsyncRatio: null,
            AvSyncDifference: null,
            CacheDuration: null,
            CacheBytes: null,
            Position: TimeSpan.Zero,
            Duration: TimeSpan.Zero,
            PercentPosition: null,
            Speed: 1,
            Volume: 100,
            IsMuted: false,
            IsPaused: false,
            SubtitleTrack: null,
            AudioTrack: null,
            Tracks: tracks,
            MpvVersion: null,
            FfmpegVersion: null,
            VoBackend: null);

        var summary = PlaybackStatsFormatter.FormatFullSummary(stats);
        Assert.Contains("... and ", summary);
        Assert.Contains("#55", summary);
    }
}
