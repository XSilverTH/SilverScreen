using SilverScreen.Core.Player;
using Xunit;

namespace SilverScreen.Tests.Player;

public sealed class PlaybackResultTests
{
    [Fact]
    public void OkResult_HasSuccessAndNoErrorMessage()
    {
        var result = PlaybackResult.Ok("Opening in MPV.");
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("Opening in MPV.", result.Message);
        Assert.True(PlaybackResult.IsSuccessStatus(result.Message));
    }

    [Fact]
    public void FailResult_HasFailureAndErrorMessage()
    {
        var result = PlaybackResult.Fail("MPV could not be started.");
        Assert.False(result.Success);
        Assert.Equal("MPV could not be started.", result.ErrorMessage);
        Assert.Equal("MPV could not be started.", result.Message);
        Assert.False(PlaybackResult.IsSuccessStatus(result.Message));
    }

    [Theory]
    [InlineData("Opening in MPV.")]
    [InlineData("Opening embedded player.")]
    [InlineData("Playback started.")]
    [InlineData("Embedded presenter called.")]
    [InlineData("External playback called.")]
    public void IsSuccessStatus_ReturnsTrueForSuccessMessages(string message)
    {
        Assert.True(PlaybackResult.IsSuccessStatus(message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Embedded playback requires libmpv.")]
    [InlineData("MPV could not be started.")]
    [InlineData("Nothing to play.")]
    public void IsSuccessStatus_ReturnsFalseForErrorOrEmptyMessages(string? message)
    {
        Assert.False(PlaybackResult.IsSuccessStatus(message));
    }
}
