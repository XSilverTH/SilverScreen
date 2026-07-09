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
        Assert.Equal("No playable URL is available for this mock video yet.", exception.Message);
    }

    [Theory]
    [InlineData("SsLinuxD01A")]
    [InlineData("SsDemoMp4A")]
    [InlineData("SsGtkBlp02B")]
    public void PlaybackRequestDoesNotDeriveWatchUrlForInternalMockIds(string mockId)
    {
        var video = CreateVideo(mockId);
        var request = new PlaybackRequest(video);

        Assert.Equal(mockId, request.VideoId);
        Assert.Null(request.PlaybackUrl);
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", true)]
    [InlineData("abc123_X-yZ", true)]
    [InlineData("SsLinuxD01A", false)]
    [InlineData("SsDemoMp4A", false)]
    [InlineData("SsGtkBlp02B", false)]
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
    [InlineData("SsDemoMp4A", "https://example.com/demo.mp4")]
    [InlineData("SsLinuxD01A", "https://test-videos.co.uk/sample.mp4")]
    [InlineData("dQw4w9WgXcQ", "https://custom-url.com/video")]
    public void MpvCommandBuilderUsesExplicitWatchUrlWhenPresent(string id, string explicitUrl)
    {
        var video = CreateVideo(id, explicitUrl);
        var request = new PlaybackRequest(video);
        var command = new MpvCommandBuilder().Build(request, new PlaybackOptions());

        var argument = Assert.Single(command.Arguments);
        Assert.Equal(explicitUrl, argument);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReturnsMockFriendlyMessageWhenPlaybackUrlIsMissing()
    {
        var video = CreateVideo("SsLinuxD01A");
        var request = new PlaybackRequest(video);
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder());

        var message = await service.PlayAsync(request);

        Assert.Equal("No playable URL is available for this mock video yet.", message);
    }

    [Fact]
    public async Task ExternalMpvPlaybackServiceReturnsCleanMessageWhenPlaybackUrlIsInvalid()
    {
        var video = CreateVideo("abc123_X-yZ", "ftp://example.com/video.mp4");
        var request = new PlaybackRequest(video);
        var service = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder());

        var message = await service.PlayAsync(request);

        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", message);
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
