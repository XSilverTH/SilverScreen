using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public void PlaybackRequestPreservesOrderedVideos()
    {
        var first = CreateVideo("abc123_X-yZ");
        var second = CreateVideo("dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ");

        var request = new PlaybackRequest([first, second]);

        Assert.Equal(new[] { first, second }, request.Videos.ToArray());
    }

    [Fact]
    public void ActivePlaybackLifecycleRestoresTheMostRecentRemainingSession()
    {
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, presence);
        var firstRequest = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);
        var secondRequest = new PlaybackRequest([CreateVideo("dQw4w9WgXcQ")]);
        var thirdRequest = new PlaybackRequest([CreateVideo("M7lc1UVf-VE")]);
        var firstStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var secondStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var thirdStartedAt = DateTimeOffset.UtcNow;

        var firstId = service.RegisterActivePlayback(firstRequest, firstStartedAt);
        var secondId = service.RegisterActivePlayback(secondRequest, secondStartedAt);
        var thirdId = service.RegisterActivePlayback(thirdRequest, thirdStartedAt);
        service.CompleteActivePlayback(secondId);

        Assert.Equal(3, presence.SetCalls.Count);
        Assert.Equal(thirdRequest, presence.SetCalls[^1].Request);

        service.CompleteActivePlayback(thirdId);

        Assert.Equal(firstRequest, presence.SetCalls[^1].Request);
        Assert.Equal(firstStartedAt, presence.SetCalls[^1].StartedAt);

        service.CompleteActivePlayback(firstId);
        Assert.Equal(1, presence.ClearCount);
        service.CompleteActivePlayback(999);
        Assert.Equal(1, presence.ClearCount);
    }

    [Fact]
    public async Task ActivePlaybackRegistrationSerializesPresencePublication()
    {
        var presence = new BlockingPresence();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, presence);
        var firstRequest = new PlaybackRequest([CreateVideo("abc123_X-yZ")]);
        var secondRequest = new PlaybackRequest([CreateVideo("dQw4w9WgXcQ")]);

        var first = Task.Run(() => service.RegisterActivePlayback(firstRequest, DateTimeOffset.UtcNow));
        Assert.True(presence.FirstSetStarted.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => service.RegisterActivePlayback(secondRequest, DateTimeOffset.UtcNow));
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(100))));

        presence.ReleaseFirstSet.Set();
        await Task.WhenAll(first, second);

        Assert.Equal([firstRequest, secondRequest], presence.SetCalls.Select(call => call.Request));
    }

    [Fact]
    public void MpvCommandBuilderUsesDefaultMpvExecutable()
    {
        var command =
            MpvCommandBuilder.Build(new PlaybackRequest([CreateVideo("abc123_X-yZ")]), new PlaybackOptions());

        Assert.Equal("mpv", command.ExecutablePath);
    }

    [Fact]
    public void MpvCommandBuilderPassesOrderedPlaylistUrlsAsSeparateArguments()
    {
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([
                CreateVideo("abc123_X-yZ"),
                CreateVideo("dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ"),
                CreateVideo("M7lc1UVf-VE")
            ]),
            new PlaybackOptions { VideoQuality = "720p" },
            "/tmp/silverscreen-cookies/cookies.txt");

        Assert.Equal(
            [
                "--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt",
                "--ytdl-format=bestvideo[height<=720]+bestaudio/best[height<=720]",
                "https://www.youtube.com/watch?v=abc123_X-yZ",
                "https://youtu.be/dQw4w9WgXcQ",
                "https://www.youtube.com/watch?v=M7lc1UVf-VE"
            ],
            command.Arguments);
    }

    [Fact]
    public void MpvCommandBuilderPassesCookiesOptionBeforeUrlWhenSessionExists()
    {
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions(),
            "/tmp/silverscreen-cookies/cookies.txt");

        Assert.Collection(
            command.Arguments,
            argument => Assert.Equal("--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt", argument),
            argument => Assert.Equal("https://www.youtube.com/watch?v=abc123_X-yZ", argument));
    }

    [Fact]
    public void MpvCommandBuilderOmitsCookiesOptionWhenSessionCookieFileIsMissing()
    {
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions());

        Assert.DoesNotContain(command.Arguments,
            argument => argument.StartsWith("--ytdl-raw-options=cookies=", StringComparison.Ordinal));
        var argument = Assert.Single(command.Arguments);
        Assert.Equal("https://www.youtube.com/watch?v=abc123_X-yZ", argument);
    }

    [Fact]
    public void MpvCommandBuilderMarksWatchedVideosWhenEnabledWithSessionCookies()
    {
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions { MarkWatchedVideos = true },
            "/tmp/silverscreen-cookies/cookies.txt");

        Assert.Contains("--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt,mark-watched=",
            command.Arguments);
    }

    [Fact]
    public void MpvCommandBuilderKeepsCookieOptionAndUrlAsSeparateStartInfoArguments()
    {
        var builder = new MpvCommandBuilder();
        var command = MpvCommandBuilder.Build(
            new PlaybackRequest([CreateVideo("abc123_X-yZ")]),
            new PlaybackOptions(),
            "/tmp/silverscreen-cookies/cookies.txt");

        var startInfo = MpvCommandBuilder.BuildStartInfo(command);

        Assert.False(startInfo.UseShellExecute);
        Assert.Collection(
            startInfo.ArgumentList,
            argument => Assert.Equal("--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt", argument),
            argument => Assert.Equal("https://www.youtube.com/watch?v=abc123_X-yZ", argument));
    }

    [Fact]
    public void MpvCommandBuilderRejectsMissingPlaybackUrlCleanly()
    {
        var request = new PlaybackRequest([CreateVideo(string.Empty)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, new PlaybackOptions()));

        Assert.Equal("No playable URL is available.", exception.Message);
    }

    [Fact]
    public void MpvCommandBuilderRejectsAnEmptyPlaylist()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(new PlaybackRequest([]), new PlaybackOptions()));

        Assert.Equal("No videos were provided for playback.", exception.Message);
    }

    [Fact]
    public void MpvCommandBuilderRejectsWholePlaylistWhenAnyUrlIsInvalid()
    {
        var request = new PlaybackRequest([
            CreateVideo("abc123_X-yZ"),
            CreateVideo("dQw4w9WgXcQ", "file:///tmp/video.mp4"),
            CreateVideo("M7lc1UVf-VE")
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, new PlaybackOptions()));

        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", exception.Message);
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", true)]
    [InlineData("abc123_X-yZ", true)]
    [InlineData("abc", false)]
    [InlineData("abc123456789", false)]
    [InlineData("abc123_X-y!", false)]
    [InlineData("abc123_X-y ", false)]
    [InlineData("", false)]
    public void LooksLikeYouTubeVideoId_ClassifiesCorrectly(string id, bool expected)
    {
        var result = PlaybackRequest.LooksLikeYouTubeVideoId(id);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", "https://custom-url.com/video")]
    [InlineData("abc", "https://custom-url.com/video2")]
    public void MpvCommandBuilderUsesExplicitWatchUrlWhenPresent(string id, string explicitUrl)
    {
        var video = CreateVideo(id, explicitUrl);
        var request = new PlaybackRequest([video]);
        var command = MpvCommandBuilder.Build(request, new PlaybackOptions());

        var argument = Assert.Single(command.Arguments);
        Assert.Equal(explicitUrl, argument);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReturnsFriendlyMessageWhenPlaybackUrlIsMissing()
    {
        var video = CreateVideo("abc");
        var request = new PlaybackRequest([video]);
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, presence);

        var message = await service.PlayAsync(request);

        Assert.Equal("No playable URL is available.", message);
        Assert.Empty(presence.SetCalls);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReturnsCleanMessageWhenPlaybackUrlIsInvalid()
    {
        var video = CreateVideo("abc123_X-yZ", "ftp://example.com/video.mp4");
        var request = new PlaybackRequest([video]);
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), null, presence);

        var message = await service.PlayAsync(request);

        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", message);
        Assert.Empty(presence.SetCalls);
    }

    [Fact]
    public void MpvCommandBuilderRejectsNonHttpPlaybackUrlCleanly()
    {
        var request = new PlaybackRequest([CreateVideo("abc123_X-yZ", "file:///tmp/video.mp4")]);

        var exception =
            Assert.Throws<InvalidOperationException>(() =>
                MpvCommandBuilder.Build(request, new PlaybackOptions()));
        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", exception.Message);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReportsMissingExecutableCleanly()
    {
        var options = new PlaybackOptions { MpvExecutablePath = "silverscreen-missing-mpv-for-test" };
        var presence = new TrackingPresence();
        var service = new ExternalMpvPlaybackService(options, new MpvCommandBuilder(), null, presence);

        var message = await service.PlayAsync(new PlaybackRequest([CreateVideo("abc123_X-yZ")]));

        Assert.Equal("Could not start MPV. Is it installed?", message);
        Assert.Empty(presence.SetCalls);
    }

    [Fact]
    public void ExternalMpvPlaybackServiceExitCleanupWithNoCookieLeaseDoesNotThrow()
    {
        var exception = Record.Exception(() => ExternalMpvPlaybackService.HandleProcessExited(null, null));

        Assert.Null(exception);
    }

    [Fact]
    public void ExternalMpvPlaybackServiceExitCleanupWithDisposedProcessDoesNotThrow()
    {
        using var process = new Process();
        process.Dispose();

        var exception = Record.Exception(() => ExternalMpvPlaybackService.HandleProcessExited(process, null));

        Assert.Null(exception);
    }

    [Fact]
    public void ExternalMpvPlaybackServiceExitCleanupDisposesCookieLease()
    {
        var lease = new TrackingDisposable();

        ExternalMpvPlaybackService.HandleProcessExited(null, lease);

        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public void ExternalMpvPlaybackServiceCookieLeaseCleanupCanBeCalledTwice()
    {
        var lease = new TrackingDisposable();

        ExternalMpvPlaybackService.CleanupCookieLeaseQuietly(lease, "test cleanup");
        ExternalMpvPlaybackService.CleanupCookieLeaseQuietly(lease, "test cleanup");

        Assert.Equal(2, lease.DisposeCount);
    }

    [Fact]
    public void ExternalMpvPlaybackServiceCookieLeaseCleanupSwallowsDisposeFailures()
    {
        var lease = new ThrowingDisposable();

        var exception =
            Record.Exception(() => ExternalMpvPlaybackService.CleanupCookieLeaseQuietly(lease, "test cleanup"));

        Assert.Null(exception);
    }

    private static VideoSummary CreateVideo(string id, string? watchUrl = null)
    {
        return new VideoSummary(id, $"Video {id}", "Test Channel", TimeSpan.FromMinutes(3), "placeholder://test", false,
            watchUrl);
    }

    private sealed class TrackingPresence : IPlaybackPresenceService
    {
        public int ClearCount { get; private set; }
        public List<(PlaybackRequest Request, DateTimeOffset StartedAt)> SetCalls { get; } = [];

        public void SetPlaying(PlaybackRequest request, DateTimeOffset startedAt)
        {
            SetCalls.Add((request, startedAt));
        }

        public void Clear()
        {
            ClearCount++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BlockingPresence : IPlaybackPresenceService
    {
        public ManualResetEventSlim FirstSetStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstSet { get; } = new();
        public List<(PlaybackRequest Request, DateTimeOffset StartedAt)> SetCalls { get; } = [];

        public void SetPlaying(PlaybackRequest request, DateTimeOffset startedAt)
        {
            lock (SetCalls)
            {
                SetCalls.Add((request, startedAt));
                if (SetCalls.Count > 1) return;
            }

            FirstSetStarted.Set();
            ReleaseFirstSet.Wait(TimeSpan.FromSeconds(5));
        }

        public void Clear()
        {
        }

        public void Dispose()
        {
            FirstSetStarted.Dispose();
            ReleaseFirstSet.Dispose();
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose()
        {
            throw new IOException("test cleanup failure");
        }
    }
}