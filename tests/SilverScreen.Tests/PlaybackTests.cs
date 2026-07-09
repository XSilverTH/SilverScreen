using SilverScreen.Core.Models;
using SilverScreen.Features.Playback;

namespace SilverScreen.Tests;

public sealed class PlaybackTests
{
    [Fact]
    public void PlaybackRequestDerivesWatchUrlFromVideoId()
    {
        var video = CreateVideo("abc123_X-yZ");
        var request = new PlaybackRequest(video);

        Assert.Equal("abc123_X-yZ", request.VideoId);
        Assert.Equal("Video abc123_X-yZ", request.Title);
        Assert.Equal("https://www.youtube.com/watch?v=abc123_X-yZ", request.PlaybackUrl);
    }

    [Fact]
    public void PlaybackRequestUsesExplicitWatchUrlWhenPresent()
    {
        var video = CreateVideo("abc123_X-yZ", "https://youtu.be/abc123_X-yZ");
        var request = new PlaybackRequest(video);

        Assert.Equal("https://youtu.be/abc123_X-yZ", request.PlaybackUrl);
    }

    [Fact]
    public void MpvCommandBuilderUsesDefaultMpvExecutable()
    {
        var command = new MpvCommandBuilder().Build(new PlaybackRequest(CreateVideo("abc123_X-yZ")), new PlaybackOptions());

        Assert.Equal("mpv", command.ExecutablePath);
    }

    [Fact]
    public void MpvCommandBuilderPassesUrlAsSeparateArgument()
    {
        var command = new MpvCommandBuilder().Build(new PlaybackRequest(CreateVideo("abc123_X-yZ")), new PlaybackOptions());

        var argument = Assert.Single(command.Arguments);
        Assert.Equal("https://www.youtube.com/watch?v=abc123_X-yZ", argument);
    }

    [Fact]
    public void MpvCommandBuilderRejectsMissingPlaybackUrlCleanly()
    {
        var request = new PlaybackRequest(CreateVideo(string.Empty));

        var exception = Assert.Throws<InvalidOperationException>(() => new MpvCommandBuilder().Build(request, new PlaybackOptions()));
        Assert.Equal("Playback URL is missing.", exception.Message);
    }

    [Fact]
    public void MpvCommandBuilderRejectsNonHttpPlaybackUrlCleanly()
    {
        var request = new PlaybackRequest(CreateVideo("abc123_X-yZ", "file:///tmp/video.mp4"));

        var exception = Assert.Throws<InvalidOperationException>(() => new MpvCommandBuilder().Build(request, new PlaybackOptions()));
        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", exception.Message);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReportsMissingExecutableCleanly()
    {
        var options = new PlaybackOptions { MpvExecutablePath = "silverscreen-missing-mpv-for-test" };
        var service = new ExternalMpvPlaybackService(options, new MpvCommandBuilder());

        var message = await service.PlayAsync(new PlaybackRequest(CreateVideo("abc123_X-yZ")));

        Assert.Equal("Could not start MPV. Is it installed?", message);
    }

    private static VideoSummary CreateVideo(string id, string? watchUrl = null)
    {
        return new VideoSummary(id, $"Video {id}", "Test Channel", TimeSpan.FromMinutes(3), "placeholder://test", false, watchUrl);
    }
}
