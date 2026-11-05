using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public sealed class PlayerErrorClassifierTests
{
    [Theory]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Sign in to confirm you’re not a bot", "Sign-in Required",
        "avatar-default-symbolic")]
    [InlineData("Sign in to confirm your age", "Sign-in Required", "avatar-default-symbolic")]
    [InlineData("Login required for age-restricted content", "Sign-in Required", "avatar-default-symbolic")]
    public void Classify_RecognizesSignInAndBotVerification(string detail, string expectedTitle, string expectedIcon)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal(expectedTitle, info.Title);
        Assert.Equal(expectedIcon, info.IconName);
        Assert.Contains("sign-in", info.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(detail, info.TechnicalDetail);
    }

    [Theory]
    [InlineData("Failed to connect to rr1---sn.googlevideo.com: Connection refused", "Connection Failed",
        "network-error-symbolic")]
    [InlineData("Network is unreachable", "Connection Failed", "network-error-symbolic")]
    [InlineData("Connection timed out", "Connection Failed", "network-error-symbolic")]
    [InlineData("Could not resolve host: www.youtube.com", "Connection Failed", "network-error-symbolic")]
    [InlineData("Name or service not known", "Connection Failed", "network-error-symbolic")]
    public void Classify_RecognizesNetworkErrors(string detail, string expectedTitle, string expectedIcon)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal(expectedTitle, info.Title);
        Assert.Equal(expectedIcon, info.IconName);
        Assert.Contains("internet connection", info.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(detail, info.TechnicalDetail);
    }

    [Theory]
    [InlineData("ERROR: [youtube] Video unavailable", "Video Unavailable", "dialog-error-symbolic")]
    [InlineData("This video is not available in your country", "Video Unavailable", "dialog-error-symbolic")]
    [InlineData("Private video. Sign in if you've been granted access", "Sign-in Required", "avatar-default-symbolic")]
    [InlineData("This video has been removed by the uploader", "Video Unavailable", "dialog-error-symbolic")]
    [InlineData("Geo-blocked content", "Video Unavailable", "dialog-error-symbolic")]
    public void Classify_RecognizesUnavailableAndRestrictedVideos(string detail, string expectedTitle,
        string expectedIcon)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal(expectedTitle, info.Title);
        Assert.Equal(expectedIcon, info.IconName);
        Assert.Equal(detail, info.TechnicalDetail);
    }

    [Theory]
    [InlineData("yt-dlp failed to extract stream formats", "Stream Extraction Failed", "dialog-error-symbolic")]
    [InlineData("ytdl_hook: format not found", "Stream Extraction Failed", "dialog-error-symbolic")]
    public void Classify_RecognizesStreamExtractionErrors(string detail, string expectedTitle, string expectedIcon)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal(expectedTitle, info.Title);
        Assert.Equal(expectedIcon, info.IconName);
        Assert.Contains("extractor", info.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(detail, info.TechnicalDetail);
    }

    [Fact]
    public void Classify_RecognizesOpenGLContextError()
    {
        var info = PlayerErrorClassifier.Classify("Unable to create an OpenGL context for embedded playback.");
        Assert.Equal("Graphics Context Error", info.Title);
        Assert.Equal("dialog-error-symbolic", info.IconName);
        Assert.Contains("OpenGL", info.Message);
    }

    [Theory]
    [InlineData("Loading failed")]
    [InlineData("Generic playback failure")]
    [InlineData("Error opening file")]
    public void Classify_FallsBackToGenericPlaybackFailure(string detail)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal("Playback Failed", info.Title);
        Assert.Equal("dialog-error-symbolic", info.IconName);
        Assert.Contains("encountered an error", info.Message);
        Assert.Equal(detail, info.TechnicalDetail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_HandlesNullOrWhitespaceGracefully(string? detail)
    {
        var info = PlayerErrorClassifier.Classify(detail);
        Assert.Equal("Playback Failed", info.Title);
        Assert.Equal("dialog-error-symbolic", info.IconName);
        Assert.False(string.IsNullOrWhiteSpace(info.Message));
        Assert.False(string.IsNullOrWhiteSpace(info.TechnicalDetail));
    }
}