using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class FullscreenPlaybackOptionTests
{
    [Fact]
    public void MpvCommandBuilderAppendsFullscreenFlagWhenOptionIsTrue()
    {
        var request = new PlaybackRequest([
            new VideoSummary("abc123def45", "Test Video", "Channel", TimeSpan.FromMinutes(3), "", false)
        ]);

        var fullscreenCommand = MpvCommandBuilder.Build(request, new PlaybackOptions { Fullscreen = true });
        Assert.Contains("--fs", fullscreenCommand.Arguments);

        var windowedCommand = MpvCommandBuilder.Build(request, new PlaybackOptions { Fullscreen = false });
        Assert.DoesNotContain("--fs", windowedCommand.Arguments);
    }
}
