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
        var service = CreateService(string.Empty, exitCode: 1, standardError: "network unavailable");

        var result = await service.SearchAsync(new SearchRequest("query"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Videos);
    }

    private static YtDlpSearchService CreateService(string output, int exitCode = 0, string standardError = "") =>
        new(new YtDlpOptions(), new FakeRunner(new ProcessResult(exitCode, output, standardError)));

    private sealed class FakeRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
