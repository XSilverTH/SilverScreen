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