using System.Collections.Immutable;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Tests.Player;

public sealed class MpvCommandBuilderTests
{
    private static VideoSummary CreateVideo(string id, string? watchUrl = null)
    {
        return new VideoSummary(id, $"Title {id}", "Channel", TimeSpan.FromMinutes(3), "", false, WatchUrl: watchUrl);
    }

    [Fact]
    public void Build_ThrowsWhenRequestIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MpvCommandBuilder.Build(null!, new PlaybackOptions()));
    }

    [Fact]
    public void Build_ThrowsWhenOptionsIsNull()
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        Assert.Throws<ArgumentNullException>(() =>
            MpvCommandBuilder.Build(request, null!));
    }

    [Fact]
    public void Build_ThrowsWhenExternalMpvDisabled()
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { ExternalMpvEnabled = false };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, options));

        Assert.Equal("External MPV playback is disabled.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_ThrowsWhenMpvExecutablePathMissing(string? executablePath)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { MpvExecutablePath = executablePath! };

        Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, options));
    }

    [Fact]
    public void Build_ThrowsWhenQueueIsEmpty()
    {
        var request = new PlaybackRequest(ImmutableArray<VideoSummary>.Empty);
        var options = new PlaybackOptions();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.Build(request, options));

        Assert.Equal(PlaybackRequest.EmptyQueueMessage, ex.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Build_Fullscreen_TogglesFullscreenArgument(bool fullscreen, bool shouldContain)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { Fullscreen = fullscreen };

        var command = MpvCommandBuilder.Build(request, options);

        if (shouldContain)
            Assert.Contains("--fs", command.Arguments);
        else
            Assert.DoesNotContain("--fs", command.Arguments);
    }

    [Theory]
    [InlineData(true, "--keep-open=yes")]
    [InlineData(false, "--keep-open=always")]
    public void Build_AutoAdvanceNextVideo_SetsCorrectKeepOpenFlag(bool autoAdvance, string expectedFlag)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { AutoAdvanceNextVideo = autoAdvance };

        var command = MpvCommandBuilder.Build(request, options);

        Assert.Contains(expectedFlag, command.Arguments);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Build_MarkWatchedVideos_TogglesMarkWatchedRawOption(bool markWatched, bool shouldContain)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { MarkWatchedVideos = markWatched };

        var command = MpvCommandBuilder.Build(request, options);

        if (shouldContain)
            Assert.Contains("--ytdl-raw-options=mark-watched=", command.Arguments);
        else
            Assert.DoesNotContain("--ytdl-raw-options=mark-watched=", command.Arguments);
    }

    [Theory]
    [InlineData("1080p", "--ytdl-format=bestvideo[height<=1080]+bestaudio/best[height<=1080]")]
    [InlineData("720p", "--ytdl-format=bestvideo[height<=720]+bestaudio/best[height<=720]")]
    [InlineData("480p", "--ytdl-format=bestvideo[height<=480]+bestaudio/best[height<=480]")]
    [InlineData("360p", "--ytdl-format=bestvideo[height<=360]+bestaudio/best[height<=360]")]
    [InlineData("Best", null)]
    public void Build_VideoQuality_SetsExpectedFormatArgument(string quality, string? expectedArg)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { VideoQuality = quality };

        var command = MpvCommandBuilder.Build(request, options);

        if (expectedArg is not null)
        {
            Assert.Contains(expectedArg, command.Arguments);
        }
        else
        {
            Assert.DoesNotContain(command.Arguments, a => a.StartsWith("--ytdl-format="));
        }
    }

    [Theory]
    [InlineData("yt-dlp", "--script-opts=ytdl_hook-ytdl_path=yt-dlp")]
    [InlineData("/usr/local/bin/yt-dlp", "--script-opts=ytdl_hook-ytdl_path=/usr/local/bin/yt-dlp")]
    public void Build_WithYtDlpExecutablePath_AddsScriptOptsArgument(string path, string expectedArg)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { YtDlpExecutablePath = path };

        var command = MpvCommandBuilder.Build(request, options);

        Assert.Contains(expectedArg, command.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithoutYtDlpExecutablePath_OmitsScriptOptsArgument(string? path)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions { YtDlpExecutablePath = path! };

        var command = MpvCommandBuilder.Build(request, options);

        Assert.DoesNotContain(command.Arguments, a => a.StartsWith("--script-opts", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithCookieFilePath_IncludesCookieArguments()
    {
        const string cookiePath = "/tmp/silverscreen/cookies.txt";
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options, cookieFilePath: cookiePath);

        Assert.Contains("--cookies", command.Arguments);
        Assert.Contains($"--cookies-file={cookiePath}", command.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithoutCookieFilePath_OmitsCookieArguments(string? cookiePath)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options, cookieFilePath: cookiePath);

        Assert.DoesNotContain("--cookies", command.Arguments);
        Assert.DoesNotContain(command.Arguments, a => a.StartsWith("--cookies-file="));
    }

    [Fact]
    public void Build_WithInputIpcServerPath_IncludesIpcServerArgument()
    {
        const string socketPath = "/tmp/silverscreen-mpv/ipc.sock";
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options, inputIpcServerPath: socketPath);

        Assert.Contains($"--input-ipc-server={socketPath}", command.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithoutInputIpcServerPath_OmitsIpcServerArgument(string? socketPath)
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options, inputIpcServerPath: socketPath);

        Assert.DoesNotContain(command.Arguments, a => a.StartsWith("--input-ipc-server="));
    }

    [Fact]
    public void Build_WithPositiveStartIndex_IncludesPlaylistStart()
    {
        var videos = ImmutableArray.Create(
            CreateVideo("video000001"),
            CreateVideo("video000002"),
            CreateVideo("video000003"));
        var request = new PlaybackRequest(videos, StartIndex: 2);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options);

        Assert.Contains("--playlist-start=2", command.Arguments);
    }

    [Fact]
    public void Build_WithZeroStartIndex_OmitsPlaylistStart()
    {
        var videos = ImmutableArray.Create(
            CreateVideo("video000001"),
            CreateVideo("video000002"));
        var request = new PlaybackRequest(videos, StartIndex: 0);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options);

        Assert.DoesNotContain(command.Arguments, a => a.StartsWith("--playlist-start="));
    }

    [Fact]
    public void Build_SingleVideo_AppendsWatchUrlAtEnd()
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678")]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options);

        Assert.Equal("https://www.youtube.com/watch?v=abc12345678", command.Arguments[^1]);
    }

    [Fact]
    public void Build_QueueItems_AppendsAllWatchUrlsInOrder()
    {
        var videos = ImmutableArray.Create(
            CreateVideo("vid11111111"),
            CreateVideo("vid22222222"),
            CreateVideo("vid33333333"));
        var request = new PlaybackRequest(videos);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options);

        var trailingUrls = command.Arguments.TakeLast(3).ToList();
        Assert.Equal(
        [
            "https://www.youtube.com/watch?v=vid11111111",
            "https://www.youtube.com/watch?v=vid22222222",
            "https://www.youtube.com/watch?v=vid33333333"
        ], trailingUrls);
    }

    [Fact]
    public void Build_VideoWithCustomWatchUrl_PreservesCustomUrl()
    {
        const string customUrl = "https://example.com/custom/watch?v=xyz";
        var request = new PlaybackRequest([CreateVideo("abc12345678", watchUrl: customUrl)]);
        var options = new PlaybackOptions();

        var command = MpvCommandBuilder.Build(request, options);

        Assert.Equal(customUrl, command.Arguments[^1]);
    }

    [Fact]
    public void GetPlaybackUrls_ThrowsWhenUrlIsNotHttpOrHttps()
    {
        var request = new PlaybackRequest([CreateVideo("abc12345678", watchUrl: "file:///local/video.mp4")]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.GetPlaybackUrls(request));

        Assert.Equal("Playback URL must be an absolute HTTP or HTTPS URL.", ex.Message);
    }

    [Fact]
    public void GetPlaybackUrls_ThrowsWhenVideoIdIsInvalidAndNoWatchUrl()
    {
        var request = new PlaybackRequest([CreateVideo("invalid-id")]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MpvCommandBuilder.GetPlaybackUrls(request));

        Assert.Equal("No playable URL is available.", ex.Message);
    }

    [Fact]
    public void BuildStartInfo_PopulatesFromCommand()
    {
        var command = new MpvPlaybackCommand("mpv", ["--fs", "--keep-open=yes", "https://example.com/video"]);

        var startInfo = MpvCommandBuilder.BuildStartInfo(command);

        Assert.Equal("mpv", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(command.Arguments, startInfo.ArgumentList);
    }
}
