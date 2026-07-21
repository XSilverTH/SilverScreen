using System.ComponentModel;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Tests;

public sealed class YtDlpSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ParsesPlaylistJsonIntoVideoSummaries()
    {
        var service = CreateService(
            """
            {
              "entries": [
                {
                  "id": "dQw4w9WgXcQ",
                  "title": "Never Gonna Give You Up",
                  "uploader": "Rick Astley",
                  "duration": 213,
                  "upload_date": "20260715",
                  "thumbnail": "https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg",
                  "webpage_url": "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
                }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("rick"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("dQw4w9WgXcQ", video.Id);
        Assert.Equal("Never Gonna Give You Up", video.Title);
        Assert.Equal("Rick Astley", video.ChannelName);
        Assert.Equal(TimeSpan.FromSeconds(213), video.Duration);
        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", video.ThumbnailUrl);
        Assert.False(video.IsShort);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", video.WatchUrl);
        Assert.Equal(new DateOnly(2026, 7, 15), video.ApproximateUploadDate);
    }

    [Fact]
    public async Task SearchAsync_SelectsTheHighestQualityThumbnail()
    {
        var service = CreateService(
            """
            {
              "entries": [
                {
                  "id": "dQw4w9WgXcQ",
                  "title": "Thumbnail selection",
                  "thumbnails": [
                    { "url": "https://thumb.url/low-preference", "preference": 1, "width": 1920, "height": 1080 },
                    { "url": "https://thumb.url/high-preference", "preference": 2, "width": 320, "height": 180 }
                  ]
                }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("thumbnails"), CancellationToken.None);

        Assert.Equal("https://thumb.url/high-preference", Assert.Single(result.Videos).ThumbnailUrl);
    }

    [Fact]
    public async Task SearchAsync_HandlesMissingOptionalFields()
    {
        var service = CreateService(
            """
            {
              "entries": [
                {
                  "id": "abcdefghijk",
                  "title": "Only required fields"
                }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("minimal"), CancellationToken.None);

        var video = Assert.Single(result.Videos);
        Assert.Equal("abcdefghijk", video.Id);
        Assert.Equal("Only required fields", video.Title);
        Assert.Equal("YouTube", video.ChannelName);
        Assert.Equal(TimeSpan.Zero, video.Duration);
        Assert.Equal(string.Empty, video.ThumbnailUrl);
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", video.WatchUrl);
        Assert.Null(video.ApproximateUploadDate);
    }

    [Fact]
    public async Task SearchAsync_KeepsMalformedUploadDateNonFatalAndNull()
    {
        var service = CreateService(
            """
            {
              "entries": [
                { "id": "malformed01", "title": "Malformed date", "upload_date": "2026-07-15" }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("malformed"), CancellationToken.None);

        var video = Assert.Single(result.Videos);
        Assert.Null(video.ApproximateUploadDate);
    }

    [Fact]
    public async Task SearchAsync_UsesNumericTimestampWhenUploadDateIsUnavailable()
    {
        var service = CreateService(
            """
            {
              "entries": [
                { "id": "timestamp01", "title": "Timestamp date", "timestamp": 1784073600 }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("timestamp"), CancellationToken.None);

        var video = Assert.Single(result.Videos);
        Assert.Equal(new DateOnly(2026, 7, 15), video.ApproximateUploadDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784073600), video.PublishedAt);
    }

    [Fact]
    public async Task SearchAsync_FiltersShortsWhenDetectable()
    {
        var service = CreateService(
            """
            {
              "entries": [
                {
                  "id": "shortsVideo1",
                  "title": "Vertical clip",
                  "webpage_url": "https://www.youtube.com/shorts/shortsVideo1"
                },
                {
                  "id": "normalVid01",
                  "title": "Normal video",
                  "is_short": false
                },
                {
                  "id": "hashShort01",
                  "title": "Tiny thing #shorts"
                }
              ]
            }
            """);

        var result = await service.SearchAsync(new SearchRequest("mixed"), CancellationToken.None);

        var video = Assert.Single(result.Videos);
        Assert.Equal("normalVid01", video.Id);
        Assert.False(video.IsShort);
    }

    [Fact]
    public async Task SearchAsync_ProducesCanonicalWatchUrlFromVideoId()
    {
        var service = CreateService(
            """
            { "entries": [ { "id": "BaW_jenozKc", "title": "Canonical" } ] }
            """);

        var result = await service.SearchAsync(new SearchRequest("canonical"), CancellationToken.None);

        var video = Assert.Single(result.Videos);
        Assert.Equal("https://www.youtube.com/watch?v=BaW_jenozKc", video.WatchUrl);
    }

    [Fact]
    public async Task SearchAsync_HandlesYtDlpMissingAsCleanFailure()
    {
        var service = new YtDlpSearchService(new YtDlpOptions(), new ThrowingRunner(new Win32Exception()));

        var result = await service.SearchAsync(new SearchRequest("linux"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Videos);
        Assert.Equal("Search failed: yt-dlp is not installed.", result.StatusMessage);
    }

    [Fact]
    public async Task SearchAsync_HandlesYtDlpFailureAsCleanFailure()
    {
        var service = CreateService(string.Empty, 1, "ERROR: network unavailable");

        var result = await service.SearchAsync(new SearchRequest("linux"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Videos);
        Assert.Equal("Search failed: ERROR: network unavailable", result.StatusMessage);
    }

    [Fact]
    public async Task SearchAsync_IgnoresStderrWarningsWhenExitCodeSucceeds()
    {
        var service = CreateService(
            """
            { "entries": [ { "id": "BaW_jenozKc", "title": "Valid despite warning" } ] }
            """,
            standardError: "WARNING: transient warning");

        var result = await service.SearchAsync(new SearchRequest("warning"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Videos);
    }

    [Fact]
    public async Task SearchAsync_ParsesJsonLinesOutput()
    {
        var service = CreateService(
            """
            { "id": "BaW_jenozKc", "title": "First line" }
            { "id": "dQw4w9WgXcQ", "title": "Second line" }
            """);

        var result = await service.SearchAsync(new SearchRequest("jsonl"), CancellationToken.None);

        Assert.Collection(
            result.Videos,
            video => Assert.Equal("BaW_jenozKc", video.Id),
            video => Assert.Equal("dQw4w9WgXcQ", video.Id));
    }

    [Fact]
    public void BuildSearchStartInfoOmitsCookiesArgumentWithoutSession()
    {
        var startInfo = YtDlpRunner.BuildSearchStartInfo(new SearchRequest("linux"), new YtDlpOptions());

        Assert.Collection(
            startInfo.ArgumentList,
            argument => Assert.Equal("--dump-single-json", argument),
            argument => Assert.Equal("--flat-playlist", argument),
            argument => Assert.Equal("--skip-download", argument),
            argument => Assert.Equal("--extractor-args", argument),
            argument => Assert.Equal("youtubetab:approximate_date", argument),
            argument => Assert.Equal("ytsearch20:linux", argument));
    }

    [Fact]
    public void BuildSearchStartInfoAddsCookiesFileArgumentWhenSessionExists()
    {
        var startInfo = YtDlpRunner.BuildSearchStartInfo(
            new SearchRequest("linux"),
            new YtDlpOptions(),
            "/tmp/silverscreen-cookies/cookies.txt");

        Assert.Collection(
            startInfo.ArgumentList,
            argument => Assert.Equal("--dump-single-json", argument),
            argument => Assert.Equal("--flat-playlist", argument),
            argument => Assert.Equal("--skip-download", argument),
            argument => Assert.Equal("--extractor-args", argument),
            argument => Assert.Equal("youtubetab:approximate_date", argument),
            argument => Assert.Equal("--cookies", argument),
            argument => Assert.Equal("/tmp/silverscreen-cookies/cookies.txt", argument),
            argument => Assert.Equal("ytsearch20:linux", argument));
    }

    private static YtDlpSearchService CreateService(string standardOutput, int exitCode = 0, string standardError = "")
    {
        return new YtDlpSearchService(new YtDlpOptions(),
            new FakeRunner(new ProcessResult(exitCode, standardOutput, standardError)));
    }

    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("http://youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("HTTPS://YOUTU.BE/dQw4w9WgXcQ", true)]
    [InlineData("hello world", false)]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://google.com", false)]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://notyoutube.com", false)]
    [InlineData("https://youtube.com.attacker.com", false)]
    [InlineData("", false)]
    public void IsLikelyYouTubeUrl_DetectsAndRejectsCorrectly(string input, bool expected)
    {
        // Arrange
        var service = CreateService("");

        // Act
        var result = service.IsLikelyYouTubeUrl(input);

        // Assert
        Assert.Equal(expected, result);
    }

    private sealed class FakeRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRunner(Exception exception) : IYtDlpRunner
    {
        public Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}