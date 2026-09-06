using System.Text.Json;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Player;

public sealed class YtDlpFormatSelectorTests
{
    private const string SampleMuxedJson = """
        {
            "id": "dQw4w9WgXcQ",
            "title": "Muxed Video",
            "formats": [
                {
                    "format_id": "18",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=18",
                    "ext": "mp4",
                    "height": 360,
                    "width": 640,
                    "fps": 30,
                    "vcodec": "avc1.42001E",
                    "acodec": "mp4a.40.2",
                    "tbr": 500
                },
                {
                    "format_id": "22",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=22",
                    "ext": "mp4",
                    "height": 720,
                    "width": 1280,
                    "fps": 30,
                    "vcodec": "avc1.64001F",
                    "acodec": "mp4a.40.2",
                    "tbr": 1500
                }
            ]
        }
        """;

    private const string SampleAdaptiveJson = """
        {
            "id": "abc12345678",
            "title": "Adaptive Streams Video",
            "formats": [
                {
                    "format_id": "137",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=137",
                    "ext": "mp4",
                    "height": 1080,
                    "width": 1920,
                    "fps": 60,
                    "vcodec": "avc1.64002a",
                    "acodec": "none",
                    "tbr": 4000
                },
                {
                    "format_id": "248",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=248",
                    "ext": "webm",
                    "height": 1080,
                    "width": 1920,
                    "fps": 30,
                    "vcodec": "vp9",
                    "acodec": "none",
                    "tbr": 2500
                },
                {
                    "format_id": "136",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=136",
                    "ext": "mp4",
                    "height": 720,
                    "width": 1280,
                    "fps": 30,
                    "vcodec": "avc1.4d401f",
                    "acodec": "none",
                    "tbr": 1500
                },
                {
                    "format_id": "135",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=135",
                    "ext": "mp4",
                    "height": 480,
                    "width": 854,
                    "fps": 30,
                    "vcodec": "avc1.4d401e",
                    "acodec": "none",
                    "tbr": 800
                },
                {
                    "format_id": "134",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=134",
                    "ext": "mp4",
                    "height": 360,
                    "width": 640,
                    "fps": 30,
                    "vcodec": "avc1.4d4015",
                    "acodec": "none",
                    "tbr": 400
                },
                {
                    "format_id": "249",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757250000&itag=249",
                    "ext": "webm",
                    "height": null,
                    "vcodec": "none",
                    "acodec": "opus",
                    "tbr": 50,
                    "abr": 50
                },
                {
                    "format_id": "251",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757190000&itag=251",
                    "ext": "webm",
                    "height": null,
                    "vcodec": "none",
                    "acodec": "opus",
                    "tbr": 160,
                    "abr": 160
                }
            ]
        }
        """;

    private const string SampleLiveDirectUrlJson = """
        {
            "id": "live1234567",
            "title": "Live Stream Direct",
            "is_live": true,
            "url": "https://manifest.googlevideo.com/api/manifest/hls_live/live.m3u8?expire=1757300000",
            "formats": []
        }
        """;

    private const string SampleLiveManifestJson = """
        {
            "id": "live7654321",
            "title": "Live Stream Manifest",
            "is_live": true,
            "formats": [
                {
                    "format_id": "hls-720",
                    "url": "https://manifest.googlevideo.com/api/manifest/hls_live/720p.m3u8?expire=1757300000",
                    "ext": "mp4",
                    "height": 720,
                    "width": 1280,
                    "fps": 30,
                    "vcodec": "avc1.4d401f",
                    "acodec": "mp4a.40.2",
                    "tbr": 2500
                }
            ]
        }
        """;

    private const string SampleDrmAndUnplayableJson = """
        {
            "id": "drm12345678",
            "title": "DRM / Storyboard Video",
            "formats": [
                {
                    "format_id": "drm-1",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=1",
                    "has_drm": true,
                    "height": 1080,
                    "vcodec": "avc1",
                    "acodec": "mp4a"
                },
                {
                    "format_id": "sb-0",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=2",
                    "format_note": "storyboard",
                    "height": 180,
                    "vcodec": "none",
                    "acodec": "none"
                },
                {
                    "format_id": "mhtml-1",
                    "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=3",
                    "protocol": "mhtml",
                    "vcodec": "none",
                    "acodec": "none"
                }
            ]
        }
        """;

    [Theory]
    [InlineData("Best", null)]
    [InlineData("1080p", "bestvideo[height<=1080]+bestaudio/best[height<=1080]")]
    [InlineData("720p", "bestvideo[height<=720]+bestaudio/best[height<=720]")]
    [InlineData("480p", "bestvideo[height<=480]+bestaudio/best[height<=480]")]
    [InlineData("360p", "bestvideo[height<=360]+bestaudio/best[height<=360]")]
    [InlineData("", null)]
    [InlineData("4K", null)]
    [InlineData("auto", null)]
    public void ToMpvFormat_MapsExpectedQualityStrings(string quality, string? expected)
    {
        var actual = YtDlpFormatSelector.ToMpvFormat(quality);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_SelectsBestQuality()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "Best");

        Assert.NotNull(media);
        Assert.Contains("itag=137", media.VideoUrl);
        Assert.NotNull(media.AudioUrl);
        Assert.Contains("itag=251", media.AudioUrl);
        Assert.Equal("Best", media.Quality);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757190000), media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_Selects1080p()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "1080p");

        Assert.NotNull(media);
        Assert.Contains("itag=137", media.VideoUrl);
        Assert.NotNull(media.AudioUrl);
        Assert.Contains("itag=251", media.AudioUrl);
        Assert.Equal("1080p", media.Quality);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_Selects720p()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "720p");

        Assert.NotNull(media);
        Assert.Contains("itag=136", media.VideoUrl);
        Assert.NotNull(media.AudioUrl);
        Assert.Contains("itag=251", media.AudioUrl);
        Assert.Equal("720p", media.Quality);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_Selects480p()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "480p");

        Assert.NotNull(media);
        Assert.Contains("itag=135", media.VideoUrl);
        Assert.NotNull(media.AudioUrl);
        Assert.Contains("itag=251", media.AudioUrl);
        Assert.Equal("480p", media.Quality);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_Selects360p()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "360p");

        Assert.NotNull(media);
        Assert.Contains("itag=134", media.VideoUrl);
        Assert.NotNull(media.AudioUrl);
        Assert.Contains("itag=251", media.AudioUrl);
        Assert.Equal("360p", media.Quality);
    }

    [Fact]
    public void SelectMedia_FallbackToMuxed_WhenNoSeparateAudioOrVideo()
    {
        var media720 = YtDlpFormatSelector.SelectMedia(SampleMuxedJson, "720p");

        Assert.NotNull(media720);
        Assert.Contains("itag=22", media720.VideoUrl);
        Assert.Null(media720.AudioUrl);
        Assert.Equal("720p", media720.Quality);

        var media360 = YtDlpFormatSelector.SelectMedia(SampleMuxedJson, "360p");

        Assert.NotNull(media360);
        Assert.Contains("itag=18", media360.VideoUrl);
        Assert.Null(media360.AudioUrl);
        Assert.Equal("360p", media360.Quality);
    }

    [Fact]
    public void SelectMedia_LiveStream_DirectUrlAtRoot()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleLiveDirectUrlJson, "Best");

        Assert.NotNull(media);
        Assert.Equal("https://manifest.googlevideo.com/api/manifest/hls_live/live.m3u8?expire=1757300000", media.VideoUrl);
        Assert.Null(media.AudioUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757300000), media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_LiveStream_FormatsManifest()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleLiveManifestJson, "720p");

        Assert.NotNull(media);
        Assert.Equal("https://manifest.googlevideo.com/api/manifest/hls_live/720p.m3u8?expire=1757300000", media.VideoUrl);
        Assert.Null(media.AudioUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757300000), media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_DrmAndUnplayable_ReturnsNull()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleDrmAndUnplayableJson, "Best");
        Assert.Null(media);
    }

    [Fact]
    public void SelectMedia_EmptyFormatsWithoutRootUrl_ReturnsNull()
    {
        var json = """{ "id": "empty123", "formats": [] }""";
        var media = YtDlpFormatSelector.SelectMedia(json, "Best");
        Assert.Null(media);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ malformed json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("{}")]
    public void SelectMedia_InvalidJsonOrMissingFormats_ReturnsNullGracefully(string invalidJson)
    {
        var media = YtDlpFormatSelector.SelectMedia(invalidJson, "720p");
        Assert.Null(media);
    }

    [Fact]
    public void SelectMedia_ExpiryTimestamp_CalculatesMinimumOfVideoAndAudio()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "1080p");

        Assert.NotNull(media);
        // Video has expire=1757200000, audio has expire=1757190000. Min is 1757190000.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757190000), media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_UrlWithoutExpire_LeavesExpiryNull()
    {
        var json = """
            {
                "id": "noexpire",
                "formats": [
                    {
                        "format_id": "1",
                        "url": "https://example.com/video.mp4",
                        "ext": "mp4",
                        "height": 720,
                        "vcodec": "avc1",
                        "acodec": "mp4a"
                    }
                ]
            }
            """;
        var media = YtDlpFormatSelector.SelectMedia(json, "720p");

        Assert.NotNull(media);
        Assert.Null(media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_PreservesPassedDetails()
    {
        var details = new YouTubeVideoDetails(
            "Description",
            10000,
            DateTimeOffset.UtcNow,
            "Test Details Title",
            "Test Channel");

        var media = YtDlpFormatSelector.SelectMedia(SampleMuxedJson, "720p", details);

        Assert.NotNull(media);
        Assert.Same(details, media.Details);
    }
}
