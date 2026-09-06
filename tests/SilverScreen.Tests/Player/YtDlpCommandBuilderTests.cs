using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Player;

public sealed class YtDlpCommandBuilderTests
{
    private const string TestExecutable = "yt-dlp";
    private const string ValidVideoId = "dQw4w9WgXcQ";

    [Fact]
    public void BuildMediaExtraction_WithoutCookies_ConstructsExpectedArguments()
    {
        var startInfo = YtDlpCommandBuilder.BuildMediaExtraction(TestExecutable, ValidVideoId);

        Assert.Equal(TestExecutable, startInfo.FileName);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);

        Assert.Contains("--dump-single-json", startInfo.ArgumentList);
        Assert.Contains("--skip-download", startInfo.ArgumentList);
        Assert.Contains("--no-playlist", startInfo.ArgumentList);
        Assert.DoesNotContain("--cookies", startInfo.ArgumentList);
        Assert.Equal($"https://www.youtube.com/watch?v={ValidVideoId}", startInfo.ArgumentList[^1]);
    }

    [Fact]
    public void BuildMediaExtraction_WithCookies_IncludesCookiesFlagAndPath()
    {
        const string cookiePath = "/tmp/silverscreen-cookies/cookies.txt";

        var startInfo = YtDlpCommandBuilder.BuildMediaExtraction(TestExecutable, ValidVideoId, cookiePath);

        Assert.Contains("--cookies", startInfo.ArgumentList);
        var cookieIndex = startInfo.ArgumentList.IndexOf("--cookies");
        Assert.True(cookieIndex >= 0 && cookieIndex < startInfo.ArgumentList.Count - 1);
        Assert.Equal(cookiePath, startInfo.ArgumentList[cookieIndex + 1]);
        Assert.Equal($"https://www.youtube.com/watch?v={ValidVideoId}", startInfo.ArgumentList[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildMediaExtraction_WithNullOrWhitespaceCookies_OmitsCookiesFlag(string? cookiePath)
    {
        var startInfo = YtDlpCommandBuilder.BuildMediaExtraction(TestExecutable, ValidVideoId, cookiePath);

        Assert.DoesNotContain("--cookies", startInfo.ArgumentList);
    }

    [Theory]
    [InlineData("dQw4w9WgXcQ", "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("-a_B1234567", "https://www.youtube.com/watch?v=-a_B1234567")]
    [InlineData("1234567890A", "https://www.youtube.com/watch?v=1234567890A")]
    public void BuildMediaExtraction_WithValidVideoId_ConstructsCorrectWatchUrl(string videoId, string expectedUrl)
    {
        var startInfo = YtDlpCommandBuilder.BuildMediaExtraction(TestExecutable, videoId);

        Assert.Equal(expectedUrl, startInfo.ArgumentList[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("waytoolongvideoid123")]
    [InlineData("abc!@#12345")]
    [InlineData("with spaces ")]
    public void BuildMediaExtraction_WithInvalidVideoId_ThrowsArgumentException(string invalidVideoId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            YtDlpCommandBuilder.BuildMediaExtraction(TestExecutable, invalidVideoId));

        Assert.Equal("videoId", exception.ParamName);
    }
}
