using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YtDlpCommentServiceTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string WatchUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    [Fact]
    public async Task GetCommentsAsync_TopUsesBoundedTopArgumentsAndDisposesCookieLease()
    {
        var cookiePath = Path.GetTempFileName();
        var cookies = new FakeCookieFileProvider(() => new CookieFileLease(cookiePath));
        var runner = new CapturingRunner(_ => Task.FromResult(Success("{ \"comments\": [] }")));
        var service = CreateService(runner, cookies);

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cookies.CreateCalls);
        Assert.Equal(TimeSpan.FromSeconds(30), runner.Timeout);
        Assert.Equal(
            [
                "--dump-single-json", "--skip-download", "--no-playlist", "--write-comments", "--extractor-args",
                "youtube:comment_sort=top;max_comments=100,100,0,0,1", "--cookies", cookiePath, WatchUrl
            ],
            runner.StartInfo!.ArgumentList);
        Assert.False(File.Exists(cookiePath));
    }

    [Fact]
    public async Task GetCommentsAsync_NewestUsesBoundedNewestArgumentsWithoutCookies()
    {
        var cookies = new FakeCookieFileProvider(() => null);
        var runner = new CapturingRunner(_ => Task.FromResult(Success("{ \"comments\": [] }")));
        var service = CreateService(runner, cookies);

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Newest);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "--dump-single-json", "--skip-download", "--no-playlist", "--write-comments", "--extractor-args",
                "youtube:comment_sort=new;max_comments=100,100,0,0,1", WatchUrl
            ],
            runner.StartInfo!.ArgumentList);
    }

    [Fact]
    public void BuildCommentsStartInfo_RejectsUnknownSort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            YtDlpRunner.BuildCommentsStartInfo("yt-dlp", VideoId, (YouTubeCommentSort)42));
    }

    [Fact]
    public async Task GetCommentsAsync_InvalidIdDoesNotCreateLeaseOrLaunchProcess()
    {
        var cookies = new FakeCookieFileProvider(() => throw new InvalidOperationException("Unexpected lease."));
        var runner = new CapturingRunner(_ => throw new InvalidOperationException("Unexpected launch."));
        var service = CreateService(runner, cookies);

        var result = await service.GetCommentsAsync("not-a-video-id", YouTubeCommentSort.Top);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Comments);
        Assert.Equal("Comments are unavailable for this video.", result.StatusMessage);
        Assert.Equal(0, cookies.CreateCalls);
        Assert.Equal(0, runner.RunCalls);
    }

    [Fact]
    public async Task GetCommentsAsync_ParsesFiltersAndNormalizesCommentsInOrder()
    {
        var service = CreateService(new CapturingRunner(_ => Task.FromResult(Success("""
            {
              "comments": [
                { "id": "first", "author": "", "text": "First text", "_time_text": "2 hours ago", "time_text": "ignored", "like_count": 7 },
                { "id": "first", "author": "Duplicate", "text": "Duplicate text", "like_count": 99 },
                { "id": " ", "author": "Skipped", "text": "No id" },
                { "id": "no-text", "author": "Skipped", "text": " " },
                { "id": "second", "author": "Author", "text": "Second text", "time_text": "yesterday", "like_count": -1 },
                { "id": "third", "author": "Third", "text": "Third text", "like_count": "not a number" },
                { "id": "fourth", "author": "Fourth", "text": "Fourth text" }
              ]
            }
            """))));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.True(result.IsSuccess);
        Assert.Equal("Comments loaded.", result.StatusMessage);
        Assert.Equal(
            ["first", "second", "third", "fourth"],
            result.Comments.Select(comment => comment.Id));
        Assert.Equal(new YouTubeComment("first", "YouTube user", "First text", "2 hours ago", 7), result.Comments[0]);
        Assert.Equal(new YouTubeComment("second", "Author", "Second text", "yesterday", 0), result.Comments[1]);
        Assert.Equal(new YouTubeComment("third", "Third", "Third text", "", 0), result.Comments[2]);
        Assert.Equal(new YouTubeComment("fourth", "Fourth", "Fourth text", "", 0), result.Comments[3]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"comments\": null }")]
    public async Task GetCommentsAsync_MissingOrNullCommentsIsSuccessfulEmpty(string output)
    {
        var service = CreateService(new CapturingRunner(_ => Task.FromResult(Success(output))));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Comments);
        Assert.Equal("No comments were returned for this video.", result.StatusMessage);
    }

    [Fact]
    public async Task GetCommentsAsync_NonArrayCommentsIsMalformedOutput()
    {
        var service = CreateService(new CapturingRunner(_ => Task.FromResult(Success("{ \"comments\": {} }"))));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Comments);
        Assert.Equal(RuntimeDependencyGuidance.YtDlpFailed("the comment output could not be read."),
            result.StatusMessage);
    }

    [Theory]
    [InlineData(2, "{ \"comments\": [] }")]
    [InlineData(0, " ")]
    [InlineData(0, "not json")]
    public async Task GetCommentsAsync_MapsProcessAndOutputFailures(int exitCode, string output)
    {
        var service =
            CreateService(new CapturingRunner(_ => Task.FromResult(new ProcessResult(exitCode, output, "error"))));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Comments);
        var expected = exitCode != 0
            ? RuntimeDependencyGuidance.YtDlpFailed($"the process exited with error code {exitCode}.")
            : string.IsNullOrWhiteSpace(output)
                ? RuntimeDependencyGuidance.YtDlpFailed("the process returned no output.")
                : RuntimeDependencyGuidance.YtDlpFailed("the comment output could not be read.");
        Assert.Equal(expected, result.StatusMessage);
    }

    [Fact]
    public async Task GetCommentsAsync_MapsTimeoutToGuidance()
    {
        var service =
            CreateService(new CapturingRunner(_ => Task.FromException<ProcessResult>(new TimeoutException())));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.False(result.IsSuccess);
        Assert.Equal(RuntimeDependencyGuidance.YtDlpTimedOut, result.StatusMessage);
    }

    [Fact]
    public async Task GetCommentsAsync_MapsUnavailableProcessToGuidance()
    {
        var service =
            CreateService(new CapturingRunner(_ => Task.FromException<ProcessResult>(new InvalidOperationException())));

        var result = await service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top);

        Assert.False(result.IsSuccess);
        Assert.Equal(RuntimeDependencyGuidance.YtDlpUnavailable("comment-yt-dlp"), result.StatusMessage);
    }

    [Fact]
    public async Task GetCommentsAsync_RethrowsCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var service = CreateService(new CapturingRunner(_ =>
            Task.FromCanceled<ProcessResult>(cancellationSource.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetCommentsAsync(VideoId, YouTubeCommentSort.Top, cancellationSource.Token));
    }

    private static YtDlpCommentService CreateService(CapturingRunner runner, ICookieFileProvider? cookies = null)
    {
        return new YtDlpCommentService(cookies ?? new FakeCookieFileProvider(() => null), "comment-yt-dlp",
            processRunner: runner);
    }

    private static ProcessResult Success(string output)
    {
        return new ProcessResult(0, output, "");
    }

    private sealed class CapturingRunner(Func<ProcessStartInfo, Task<ProcessResult>> run) : IYtDlpProcessRunner
    {
        public ProcessStartInfo? StartInfo { get; private set; }
        public int RunCalls { get; private set; }
        public TimeSpan? Timeout { get; private set; }


        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RunCalls++;
            StartInfo = startInfo;
            Timeout = timeout;
            return run(startInfo);
        }
    }

    private sealed class FakeCookieFileProvider(Func<CookieFileLease?> create) : ICookieFileProvider
    {
        public int CreateCalls { get; private set; }

        public CookieFileLease? CreateCookieFile()
        {
            CreateCalls++;
            return create();
        }
    }
}