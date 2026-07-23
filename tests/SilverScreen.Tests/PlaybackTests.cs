using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class PlaybackTests
{
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
                "--fs",
                "--ytdl-raw-options=cookies=/tmp/silverscreen-cookies/cookies.txt",
                "--ytdl-format=bestvideo[height<=720]+bestaudio/best[height<=720]",
                "https://www.youtube.com/watch?v=abc123_X-yZ",
                "https://youtu.be/dQw4w9WgXcQ",
                "https://www.youtube.com/watch?v=M7lc1UVf-VE"
            ],
            command.Arguments);
    }

    [Fact]
    public void PlaybackUrlAndQualityHelpersMatchTheExternalMpvContract()
    {
        var urls = MpvCommandBuilder.GetPlaybackUrls(new PlaybackRequest([
            CreateVideo("abc123_X-yZ"),
            CreateVideo("dQw4w9WgXcQ", "https://example.test/video"),
            CreateVideo("M7lc1UVf-VE", "https://youtu.be/M7lc1UVf-VE")
        ]));

        Assert.Equal([
            "https://www.youtube.com/watch?v=abc123_X-yZ",
            "https://example.test/video",
            "https://youtu.be/M7lc1UVf-VE"
        ], urls);
        Assert.Null(MpvCommandBuilder.BuildYtdlFormat("Best"));
        Assert.Equal("bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            MpvCommandBuilder.BuildYtdlFormat("1080p"));
        Assert.Equal("bestvideo[height<=720]+bestaudio/best[height<=720]", MpvCommandBuilder.BuildYtdlFormat("720p"));
        Assert.Equal("bestvideo[height<=480]+bestaudio/best[height<=480]", MpvCommandBuilder.BuildYtdlFormat("480p"));
        Assert.Equal("bestvideo[height<=360]+bestaudio/best[height<=360]", MpvCommandBuilder.BuildYtdlFormat("360p"));
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
}