using SilverScreen.Player.RichText;

namespace SilverScreen.Tests.Player.RichText;

public sealed class PlayerLinkRouterTests
{
    private readonly List<string> _channels = [];
    private readonly List<string> _externals = [];
    private readonly PlayerLinkRouter _router;
    private readonly List<string> _searches = [];
    private readonly List<double> _seeks = [];
    private readonly List<string> _videos = [];

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

public sealed class RichLabelMarkupTests
{
    [Fact]
    public void Build_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RichLabelMarkup.Build(null));
        Assert.Equal(string.Empty, RichLabelMarkup.Build(string.Empty));
    }

    [Fact]
    public void Build_EscapesAllXmlSpecialCharactersInTextAndLinks()
    {
        var input = "A & B < C > D \"quote\" 'apos' https://example.com/?a=1&b=2";
        var markup = RichLabelMarkup.Build(input);

        Assert.Contains("A &amp; B &lt; C &gt; D &quot;quote&quot; &apos;apos&apos;", markup);
        Assert.Contains("<a href=\"https://example.com/?a=1&amp;b=2\">https://example.com/?a=1&amp;b=2</a>", markup);
    }

    [Fact]
    public void Build_FormatsTimestampHashtagAndMentionLinks()
    {
        var markup = RichLabelMarkup.Build("Jump to 1:23 #dotnet @example");

        Assert.Contains("<a href=\"seek:83\">1:23</a>", markup);
        Assert.Contains("<a href=\"hashtag:dotnet\">#dotnet</a>", markup);
        Assert.Contains("<a href=\"https://www.youtube.com/@example\">@example</a>", markup);
    }
}