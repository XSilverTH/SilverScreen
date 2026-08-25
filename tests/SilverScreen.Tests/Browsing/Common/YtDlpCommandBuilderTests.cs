using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Browsing.Common;

public sealed class YtDlpCommandBuilderTests
{
    [Fact]
    public void BuildHome_WithCustomCount_SetsCorrectPlaylistRange()
    {
        var startInfo = YtDlpCommandBuilder.BuildHome("yt-dlp", startIndex: 1, count: 40);

        Assert.Contains("--playlist-start", startInfo.ArgumentList);
        Assert.Contains("1", startInfo.ArgumentList);
        Assert.Contains("--playlist-end", startInfo.ArgumentList);
        Assert.Contains("40", startInfo.ArgumentList);
        Assert.Contains(":ytrec", startInfo.ArgumentList);
    }

    [Fact]
    public void BuildHome_WithOneVideoPerRow_SetsEightVideosRange()
    {
        var startInfo = YtDlpCommandBuilder.BuildHome("yt-dlp", startIndex: 9, count: 8);

        Assert.Contains("--playlist-start", startInfo.ArgumentList);
        Assert.Contains("9", startInfo.ArgumentList);
        Assert.Contains("--playlist-end", startInfo.ArgumentList);
        Assert.Contains("16", startInfo.ArgumentList);
    }

    [Fact]
    public void BuildHistory_WithCustomCount_SetsCorrectPlaylistRange()
    {
        var startInfo = YtDlpCommandBuilder.BuildHistory("yt-dlp", startIndex: 1, count: 40, "fake-cookie-path");

        Assert.Contains("--playlist-start", startInfo.ArgumentList);
        Assert.Contains("1", startInfo.ArgumentList);
        Assert.Contains("--playlist-end", startInfo.ArgumentList);
        Assert.Contains("40", startInfo.ArgumentList);
        Assert.Contains("https://www.youtube.com/feed/history", startInfo.ArgumentList);
    }

    [Fact]
    public void BuildSearch_WithCustomCount_SetsSearchLimitAndRange()
    {
        var request = new SearchRequest("test query", StartIndex: 1, Count: 40);
        var options = new YtDlpOptions { ExecutablePath = "yt-dlp" };

        var startInfo = YtDlpCommandBuilder.BuildSearch(request, options);

        Assert.Contains("--playlist-start", startInfo.ArgumentList);
        Assert.Contains("1", startInfo.ArgumentList);
        Assert.Contains("--playlist-end", startInfo.ArgumentList);
        Assert.Contains("40", startInfo.ArgumentList);
        Assert.Contains("ytsearch40:test query", startInfo.ArgumentList);
    }

    [Fact]
    public void BuildChannel_WithCustomCount_SetsCorrectPlaylistRange()
    {
        var options = new YtDlpOptions { ExecutablePath = "yt-dlp" };
        var startInfo = YtDlpCommandBuilder.BuildChannel(
            "https://www.youtube.com/@example",
            ChannelVideoSort.Newest,
            options,
            startIndex: 1,
            count: 40);

        Assert.Contains("--playlist-start", startInfo.ArgumentList);
        Assert.Contains("1", startInfo.ArgumentList);
        Assert.Contains("--playlist-end", startInfo.ArgumentList);
        Assert.Contains("40", startInfo.ArgumentList);
    }
}
