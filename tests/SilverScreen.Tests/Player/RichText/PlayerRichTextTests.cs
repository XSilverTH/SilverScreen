using SilverScreen.Player.RichText;

namespace SilverScreen.Tests.Player.RichText;

public sealed class RichLabelMarkupTests
{
    [Fact]
    public void Build_EscapesPlainText()
    {
        var markup = RichLabelMarkup.Build("Fish & <chips> \"quoted\"");

        Assert.Equal("Fish &amp; &lt;chips&gt; &quot;quoted&quot;", markup);
    }

    [Fact]
    public void Build_LinksTimestampToSeekUri()
    {
        var markup = RichLabelMarkup.Build("Jump to 1:23 now");

        Assert.Equal("Jump to <a href=\"seek:83\">1:23</a> now", markup);
    }

    [Fact]
    public void Build_LinksUrlsHashtagsAndMentions()
    {
        var markup = RichLabelMarkup.Build("See https://example.com/a?b=1&c=2 #tag @user");

        Assert.Equal(
            "See <a href=\"https://example.com/a?b=1&amp;c=2\">https://example.com/a?b=1&amp;c=2</a> " +
            "<a href=\"hashtag:tag\">#tag</a> " +
            "<a href=\"https://www.youtube.com/@user\">@user</a>",
            markup);
    }
}

public sealed class PlayerLinkRouterTests
{
    private readonly List<double> _seeks = [];
    private readonly List<string> _videos = [];
    private readonly List<string> _channels = [];
    private readonly List<string> _searches = [];
    private readonly List<string> _externals = [];
    private readonly PlayerLinkRouter _router;

    public PlayerLinkRouterTests()
    {
        _router = new PlayerLinkRouter(
            _seeks.Add,
            video => _videos.Add(video.Id),
            (url, _) => _channels.Add(url),
            _searches.Add,
            _externals.Add);
    }

    [Fact]
    public void Activate_SeekUri_RequestsSeek()
    {
        Assert.True(_router.Activate("seek:83"));

        Assert.Equal([83], _seeks);
        Assert.Empty(_externals);
    }

    [Fact]
    public void Activate_HashtagUri_RequestsSearch()
    {
        Assert.True(_router.Activate("hashtag:gaming"));

        Assert.Equal(["gaming"], _searches);
    }

    [Fact]
    public void Activate_VideoUrl_RequestsPlayback()
    {
        Assert.True(_router.Activate("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Equal(["dQw4w9WgXcQ"], _videos);
    }

    [Fact]
    public void Activate_ChannelUrl_RequestsChannel()
    {
        Assert.True(_router.Activate("https://www.youtube.com/@somehandle"));

        Assert.Equal(["https://www.youtube.com/@somehandle"], _channels);
    }

    [Fact]
    public void Activate_OtherWebUrl_OpensExternally()
    {
        Assert.True(_router.Activate("https://example.com/article"));

        Assert.Equal(["https://example.com/article"], _externals);
    }

    [Fact]
    public void Activate_PlaylistUrl_OpensExternally()
    {
        Assert.True(_router.Activate("https://www.youtube.com/playlist?list=PL123"));

        Assert.Equal(["https://www.youtube.com/playlist?list=PL123"], _externals);
    }
}
