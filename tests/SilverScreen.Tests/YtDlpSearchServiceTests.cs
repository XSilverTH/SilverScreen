using System.ComponentModel;
using SilverScreen.Core.Models;
using SilverScreen.Features.Search;

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
        var service = CreateService(string.Empty, exitCode: 1, standardError: "ERROR: network unavailable");

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

    private static YtDlpSearchService CreateService(string standardOutput, int exitCode = 0, string standardError = "")
    {
        return new YtDlpSearchService(new YtDlpOptions(), new FakeRunner(new ProcessResult(exitCode, standardOutput, standardError)));
    }

    private sealed class FakeRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRunner(Exception exception) : IYtDlpRunner
    {
        public Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options, CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}
