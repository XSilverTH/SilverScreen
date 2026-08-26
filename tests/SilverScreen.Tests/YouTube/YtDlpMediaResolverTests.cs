using System.Diagnostics;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.YouTube;

public sealed class YtDlpMediaResolverTests
{
    private const string SampleJson = """
        {
            "id": "abc12345678",
            "title": "Test Video Title",
            "description": "Test Description",
            "uploader": "Test Channel",
            "uploader_id": "testchannel",
            "view_count": 12345,
            "timestamp": 1700000000,
            "formats": [
                {
                    "format_id": "140",
                    "vcodec": "none",
                    "acodec": "mp4a.40.2",
                    "abr": 128,
                    "url": "https://googlevideo.com/audio140?expire=2000000000"
                },
                {
                    "format_id": "137",
                    "vcodec": "avc1.640028",
                    "acodec": "none",
                    "height": 1080,
                    "fps": 30,
                    "tbr": 4000,
                    "url": "https://googlevideo.com/video1080?expire=2000000000"
                },
                {
                    "format_id": "136",
                    "vcodec": "avc1.4d401f",
                    "acodec": "none",
                    "height": 720,
                    "fps": 30,
                    "tbr": 2000,
                    "url": "https://googlevideo.com/video720?expire=2000000000"
                },
                {
                    "format_id": "18",
                    "vcodec": "avc1.42001E",
                    "acodec": "mp4a.40.2",
                    "height": 360,
                    "fps": 30,
                    "tbr": 600,
                    "url": "https://googlevideo.com/muxed360?expire=2000000000"
                }
            ]
        }
        """;

    [Fact]
    public async Task SingleExtractionServesBothMediaResolutionAndVideoDetails()
    {
        var runner = new CountingRunner(SampleJson);
        var preferences = new TestPreferences(new AppPreferences { VideoQuality = "1080p" });
        var resolver = new YtDlpMediaResolver(
            new FakeCookieFileProvider(() => null),
            preferences,
            runner);

        // 1. Resolve media (playback path)
        var mediaResult = await resolver.ResolveMediaAsync("abc12345678");
        Assert.True(mediaResult.IsSuccess);
        Assert.NotNull(mediaResult.Media);
        Assert.Equal("https://googlevideo.com/video1080?expire=2000000000", mediaResult.Media.VideoUrl);
        Assert.Equal("https://googlevideo.com/audio140?expire=2000000000", mediaResult.Media.AudioUrl);
        Assert.Equal(1, runner.ExecutionCount);

        // 2. Get video details (details panel path)
        var detailsResult = await resolver.GetVideoDetailsAsync("abc12345678");
        Assert.True(detailsResult.IsSuccess);
        Assert.NotNull(detailsResult.Details);
        Assert.Equal("Test Video Title", detailsResult.Details.Title);
        Assert.Equal("Test Description", detailsResult.Details.Description);
        Assert.Equal(12345, detailsResult.Details.ViewCount);

        // Crucial: exactly ONE yt-dlp execution occurred
        Assert.Equal(1, runner.ExecutionCount);
    }

    [Fact]
    public async Task QualityPreferencesFilterSelectedVideoStream()
    {
        var runner = new CountingRunner(SampleJson);
        var preferences = new TestPreferences(new AppPreferences { VideoQuality = "720p" });
        var resolver = new YtDlpMediaResolver(
            new FakeCookieFileProvider(() => null),
            preferences,
            runner);

        var mediaResult = await resolver.ResolveMediaAsync("abc12345678");
        Assert.True(mediaResult.IsSuccess);
        Assert.NotNull(mediaResult.Media);
        Assert.Equal("https://googlevideo.com/video720?expire=2000000000", mediaResult.Media.VideoUrl);
        Assert.Equal("https://googlevideo.com/audio140?expire=2000000000", mediaResult.Media.AudioUrl);
    }

    [Fact]
    public async Task DirectUrlPassedToMpvDisablesYtdlHook()
    {
        var runner = new CountingRunner(SampleJson);
        var preferences = new TestPreferences(new AppPreferences { VideoQuality = "1080p", PlaybackBackend = PlaybackBackends.ExternalMpv });
        var resolver = new YtDlpMediaResolver(
            new FakeCookieFileProvider(() => null),
            preferences,
            runner);

        var res = await resolver.ResolveMediaAsync("abc12345678");
        Assert.True(res.IsSuccess);

        var request = new PlaybackRequest([new VideoSummary("abc12345678", "Test Video", "Channel", TimeSpan.FromMinutes(1), "https://thumb", false)]);
        var options = new PlaybackOptions
        {
            MpvExecutablePath = "mpv",
            VideoQuality = "1080p",
            ExternalMpvEnabled = true
        };

        var command = MpvCommandBuilder.Build(request, options, resolvedMediaItems: [res.Media!]);

        Assert.Contains("--ytdl=no", command.Arguments);
        Assert.Contains($"--audio-file={res.Media!.AudioUrl}", command.Arguments);
        Assert.Contains(res.Media!.VideoUrl, command.Arguments);
        Assert.DoesNotContain(PlaybackRequest.BuildWatchUrl("abc12345678")!, command.Arguments);
    }

    [Fact]
    public async Task StaleExpiredMediaTriggersReExtraction()
    {
        // Expired timestamp: 1700000000 (past)
        var expiredJson = SampleJson.Replace("2000000000", "1700000000");
        var runner = new CountingRunner(expiredJson);
        var preferences = new TestPreferences(new AppPreferences { VideoQuality = "1080p" });
        var resolver = new YtDlpMediaResolver(
            new FakeCookieFileProvider(() => null),
            preferences,
            runner);

        // First resolve
        var first = await resolver.ResolveMediaAsync("abc12345678");
        Assert.True(first.IsSuccess);
        Assert.Equal(1, runner.ExecutionCount);

        // Second resolve: since URL expiry was in the past, cache entry is expired so re-extraction is triggered
        var second = await resolver.ResolveMediaAsync("abc12345678");
        Assert.True(second.IsSuccess);
        Assert.Equal(2, runner.ExecutionCount);
    }

    private sealed class CountingRunner(string output) : IYtDlpRunner
    {
        public int ExecutionCount { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ProcessResult(0, output, string.Empty));
        }
    }

    private sealed class FakeCookieFileProvider(Func<CookieFileLease?> create) : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile() => create();
    }

    private sealed class TestPreferences(AppPreferences? preferences = null) : IPreferencesService
    {
        private readonly AppPreferences _preferences = preferences ?? new AppPreferences();

        public event EventHandler<AppPreferences>? PreferencesChanged
        {
            add { }
            remove { }
        }

        public void SavePreferences(AppPreferences preferences) { }
        public AppPreferences GetPreferences() => _preferences;
    }
}
