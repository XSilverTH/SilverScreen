using SilverScreen.Core.Preferences;
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

    private const string OverCapAdaptiveWithCompliantMuxedJson = """
                                                                 {
                                                                     "id": "quality-cap-regression",
                                                                     "formats": [
                                                                         {
                                                                             "format_id": "1080",
                                                                             "url": "https://rr1.googlevideo.com/videoplayback?expire=1757200000&itag=1080",
                                                                             "ext": "mp4",
                                                                             "height": 1080,
                                                                             "width": 1920,
                                                                             "fps": 30,
                                                                             "vcodec": "avc1.64002a",
                                                                             "acodec": "none",
                                                                             "tbr": 4000
                                                                         },
                                                                         {
                                                                             "format_id": "251",
                                                                             "url": "https://rr1.googlevideo.com/videoplayback?expire=1757190000&itag=251",
                                                                             "ext": "webm",
                                                                             "vcodec": "none",
                                                                             "acodec": "opus",
                                                                             "tbr": 160
                                                                         },
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
    [InlineData("4320p", "bestvideo[height<=4320]+bestaudio/best[height<=4320]")]
    [InlineData("2160p", "bestvideo[height<=2160]+bestaudio/best[height<=2160]")]
    [InlineData("1440p", "bestvideo[height<=1440]+bestaudio/best[height<=1440]")]
    [InlineData("1080p", "bestvideo[height<=1080]+bestaudio/best[height<=1080]")]
    [InlineData("864p", "bestvideo[height<=864]+bestaudio/best[height<=864]")]
    [InlineData("720p", "bestvideo[height<=720]+bestaudio/best[height<=720]")]
    [InlineData("480p", "bestvideo[height<=480]+bestaudio/best[height<=480]")]
    [InlineData("360p", "bestvideo[height<=360]+bestaudio/best[height<=360]")]
    [InlineData("240p", "bestvideo[height<=240]+bestaudio/best[height<=240]")]
    [InlineData("144p", "bestvideo[height<=144]+bestaudio/best[height<=144]")]
    [InlineData("4K", "bestvideo[height<=2160]+bestaudio/best[height<=2160]")]
    [InlineData("2K", "bestvideo[height<=1440]+bestaudio/best[height<=1440]")]
    [InlineData("8K", "bestvideo[height<=4320]+bestaudio/best[height<=4320]")]
    [InlineData("1080p60", "bestvideo[height<=1080]+bestaudio/best[height<=1080]")]
    [InlineData("2160p (4K)", "bestvideo[height<=2160]+bestaudio/best[height<=2160]")]
    [InlineData("", null)]
    [InlineData("auto", null)]
    public void ToMpvFormat_MapsExpectedQualityStrings(string quality, string? expected)
    {
        var actual = YtDlpFormatSelector.ToMpvFormat(quality);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToMpvFormat_WithPreferredCodec_GeneratesFallbackChain()
    {
        var av1_1080 = YtDlpFormatSelector.ToMpvFormat("1080p", PreferredVideoCodec.Av1);
        Assert.Equal("bestvideo[vcodec^=av01][height<=1080]+bestaudio/bestvideo[height<=1080]+bestaudio/best[height<=1080]", av1_1080);

        var av1_best = YtDlpFormatSelector.ToMpvFormat("Best", PreferredVideoCodec.Av1);
        Assert.Equal("bestvideo[vcodec^=av01]+bestaudio/bestvideo+bestaudio/best", av1_best);

        var vp9_1080 = YtDlpFormatSelector.ToMpvFormat("1080p", PreferredVideoCodec.Vp9);
        Assert.Equal("bestvideo[vcodec^=vp09][height<=1080]+bestaudio/bestvideo[vcodec^=vp9][height<=1080]+bestaudio/bestvideo[height<=1080]+bestaudio/best[height<=1080]", vp9_1080);

        var h264_720 = YtDlpFormatSelector.ToMpvFormat("720p", PreferredVideoCodec.H264);
        Assert.Equal("bestvideo[vcodec^=avc1][height<=720]+bestaudio/bestvideo[vcodec^=h264][height<=720]+bestaudio/bestvideo[height<=720]+bestaudio/best[height<=720]", h264_720);
    }

    [Fact]
    public void GetAvailableQualityTracks_FromAdaptiveJson_ReturnsOrderedTracks()
    {
        var tracks = YtDlpFormatSelector.GetAvailableQualityTracks(SampleAdaptiveJson);
        Assert.Equal(["1080p60", "720p", "480p", "360p"], tracks);
    }

    [Fact]
    public void GetAvailableQualityTracks_WithWeirdResolution_DoesNotMaskWeirdResolution()
    {
        var json = """
        {
            "id": "weird-res",
            "formats": [
                { "format_id": "8k", "height": 4320, "fps": 60, "vcodec": "av01" },
                { "format_id": "4k", "height": 2160, "fps": 60, "vcodec": "vp9" },
                { "format_id": "2k", "height": 1440, "fps": 30, "vcodec": "vp9" },
                { "format_id": "weird", "height": 864, "fps": 30, "vcodec": "avc1" },
                { "format_id": "720", "height": 720, "fps": 30, "vcodec": "avc1" }
            ]
        }
        """;

        var tracks = YtDlpFormatSelector.GetAvailableQualityTracks(json);
        Assert.Equal(["4320p60", "2160p60", "1440p", "864p", "720p"], tracks);
    }

    [Fact]
    public void SelectMedia_AdaptiveStreams_WithVp9Preferred_SelectsVp9Stream()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleAdaptiveJson, "1080p", preferredCodec: PreferredVideoCodec.Vp9);

        Assert.NotNull(media);
        Assert.Contains("itag=248", media.VideoUrl);
        Assert.Contains("itag=251", media.AudioUrl);
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
    public void SelectMedia_QualityCap_PrefersCompliantMuxedOverOverCapAdaptive()
    {
        var media = YtDlpFormatSelector.SelectMedia(OverCapAdaptiveWithCompliantMuxedJson, "360p");

        Assert.NotNull(media);
        Assert.Contains("itag=18", media.VideoUrl);
        Assert.Null(media.AudioUrl);
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
        Assert.Equal("https://manifest.googlevideo.com/api/manifest/hls_live/live.m3u8?expire=1757300000",
            media.VideoUrl);
        Assert.Null(media.AudioUrl);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1757300000), media.ExpiresAt);
    }

    [Fact]
    public void SelectMedia_LiveStream_FormatsManifest()
    {
        var media = YtDlpFormatSelector.SelectMedia(SampleLiveManifestJson, "720p");

        Assert.NotNull(media);
        Assert.Equal("https://manifest.googlevideo.com/api/manifest/hls_live/720p.m3u8?expire=1757300000",
            media.VideoUrl);
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
}