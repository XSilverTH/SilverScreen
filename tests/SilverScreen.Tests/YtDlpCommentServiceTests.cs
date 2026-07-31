using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YtDlpCommentServiceTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string WatchUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";


    [Fact]
    public async Task GetCommentsAsync_ParsesFiltersRelationshipsAndNormalizesCommentsInOrder()
    {
        var service = CreateService(new CapturingRunner(_ => Task.FromResult(Success("""
            {
              "comments": [
                { "id": "first", "parent": "root", "author": "", "text": "First text", "_time_text": "2 hours ago", "time_text": "ignored", "like_count": 7 },
                { "id": "first", "author": "Duplicate", "text": "Duplicate text", "like_count": 99 },
                { "id": " ", "author": "Skipped", "text": "No id" },
                { "id": "no-text", "author": "Skipped", "text": " " },
                { "id": "second", "parent": "first", "author": "Author", "text": "Second text", "time_text": "yesterday", "like_count": -1 },
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
        Assert.Equal(new YouTubeComment("second", "Author", "Second text", "yesterday", 0, "first"),
            result.Comments[1]);
        Assert.Equal(new YouTubeComment("third", "Third", "Third text", "", 0), result.Comments[2]);
        Assert.Equal(new YouTubeComment("fourth", "Fourth", "Fourth text", "", 0), result.Comments[3]);
    }


    private static YtDlpCommentService CreateService(CapturingRunner runner, ICookieFileProvider? cookies = null)
    {
        return new YtDlpCommentService(cookies ?? new FakeCookieFileProvider(() => null),
            new TestPreferences("comment-yt-dlp"), runner);
    }

    private static ProcessResult Success(string output)
    {
        return new ProcessResult(0, output, "");
    }

    private sealed class CapturingRunner(Func<ProcessStartInfo, Task<ProcessResult>> run) : IYtDlpRunner
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

    private sealed class TestPreferences(string executablePath) : IPreferencesService
    {
        private readonly AppPreferences _preferences = new() { YtDlpExecutablePath = executablePath };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            PreferencesChanged?.Invoke(this, preferences);
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