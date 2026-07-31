using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Tests;

public sealed class YtDlpSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_MapsSuccessfulYtDlpResults()
    {
        var service = CreateService("""
                                    { "entries": [
                                      { "id": "dQw4w9WgXcQ", "title": "Video", "uploader": "Channel", "duration": 213 }
                                    ] }
                                    """);

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("dQw4w9WgXcQ", video.Id);
        Assert.Equal("Video", video.Title);
        Assert.Equal(TimeSpan.FromSeconds(213), video.Duration);
    }

    [Fact]
    public async Task SearchAsync_FiltersDetectableShorts()
    {
        var service = CreateService("""
                                    { "entries": [
                                      { "id": "shortVideo1", "title": "Short", "webpage_url": "https://youtube.com/shorts/shortVideo1" },
                                      { "id": "normalVid01", "title": "Normal" }
                                    ] }
                                    """);

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.Equal(["normalVid01"], result.Videos.Select(video => video.Id));
    }

    [Fact]
    public async Task SearchAsync_ReportsRunnerFailureWithoutResults()
    {
        var service = CreateService(string.Empty, 1, "network unavailable");

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Videos);
    }

    [Fact]
    public void BuildHome_RequestsTwentyRecommendations()
    {
        var command = YtDlpCommandBuilder.BuildHome("yt-dlp", 1);

        Assert.Equal(["--dump-single-json", "--flat-playlist", "--skip-download", "--extractor-args",
            "youtubetab:approximate_date", "--playlist-start", "1", "--playlist-end", "20", ":ytrec"],
            command.ArgumentList);
    }

    [Fact]
    public void BuildSearch_UsesRequestedPageRange()
    {
        var command = YtDlpCommandBuilder.BuildSearch(new SearchRequest("query", 21),
            new YtDlpOptions { MaxResults = 20 });

        Assert.Contains("--playlist-start", command.ArgumentList);
        Assert.Contains("--playlist-end", command.ArgumentList);
        Assert.Equal("ytsearch40:query", command.ArgumentList.Last());
    }

    private static YtDlpSearchService CreateService(string output, int exitCode = 0, string standardError = "")
    {
        return new YtDlpSearchService(new YtDlpOptions(),
            new FakeRunner(new ProcessResult(exitCode, output, standardError)));
    }

    private sealed class FakeRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }
}