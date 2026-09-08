using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class YouTubeRichTextTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsNoSegments()
    {
        Assert.Empty(YouTubeRichText.Parse(null));
        Assert.Empty(YouTubeRichText.Parse(string.Empty));
    }

    [Fact]
    public void Parse_PlainText_ReturnsSinglePlainSegment()
    {
        var segments = YouTubeRichText.Parse("Just a plain comment.");

        var segment = Assert.Single(segments);
        Assert.Equal("Just a plain comment.", segment.Text);
        Assert.Null(segment.Link);
    }

    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("1:23", 83)]
    [InlineData("01:02:03", 3723)]
    [InlineData("1:02:03", 3723)]
    [InlineData("75:30", 4530)]
    [InlineData("(2:04)", 124)]
    public void Parse_LinksTimestamps(string input, int expectedSeconds)
    {
        var segments = YouTubeRichText.Parse($"Intro {input} outro");

        var link = Assert.Single(segments, segment => segment.Link is not null);
        Assert.Equal(YouTubeRichTextKind.Timestamp, link.Link!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), link.Link.Timestamp);
        Assert.Null(segments[0].Link);
        Assert.Null(segments[^1].Link);
    }

    [Theory]
    [InlineData("1:2")]
    [InlineData("12:345")]
    [InlineData("10:75")]
    [InlineData("2024")]
    [InlineData("1:02:03:04")]
    [InlineData("version 2")]
    public void Parse_IgnoresNonTimestamps(string input)
    {
        var segments = YouTubeRichText.Parse($"See {input} here");

        Assert.All(segments, segment => Assert.Null(segment.Link));
    }

    [Theory]
    [InlineData("https://example.com/page.", "https://example.com/page")]
    [InlineData("See (https://example.com/a(b))!", "https://example.com/a(b)")]
    [InlineData("Visit www.example.com/a?b=1&c=2!", "https://www.example.com/a?b=1&c=2")]
    [InlineData("Watch youtube.com/watch?v=dQw4w9WgXcQ.", "https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("Clip youtu.be/dQw4w9WgXcQ?t=42", "https://youtu.be/dQw4w9WgXcQ?t=42")]
    public void Parse_LinksUrlsAndTrimsTrailingPunctuation(string input, string expectedUrl)
    {
        var segments = YouTubeRichText.Parse(input);

        var link = Assert.Single(segments, segment => segment.Link is not null);
        Assert.Equal(YouTubeRichTextKind.Url, link.Link!.Kind);
        Assert.Equal(expectedUrl, link.Link.Value);
    }

    [Fact]
    public void Parse_DoesNotLinkTimestampsInsideUrls()
    {
        var segments = YouTubeRichText.Parse("https://example.com/12:34?t=1:23");

        var link = Assert.Single(segments, segment => segment.Link is not null);
        Assert.Equal(YouTubeRichTextKind.Url, link.Link!.Kind);
    }

    [Fact]
    public void Parse_LinksHashtagButNotLanguageFragmentOrDigits()
    {
        var segments = YouTubeRichText.Parse("Love #gaming but C# and #123 stay plain");

        var link = Assert.Single(segments, segment => segment.Link is not null);
        Assert.Equal(YouTubeRichTextKind.Hashtag, link.Link!.Kind);
        Assert.Equal("#gaming", link.Text);
        Assert.Equal("gaming", link.Link.Value);
    }

    [Fact]
    public void Parse_LinksMentionButNotEmailAddress()
    {
        var segments = YouTubeRichText.Parse("Thanks @somehandle, not user@example.com");

        var link = Assert.Single(segments, segment => segment.Link is not null);
        Assert.Equal(YouTubeRichTextKind.Mention, link.Link!.Kind);
        Assert.Equal("@somehandle", link.Text);
        Assert.Equal("somehandle", link.Link.Value);
    }

    [Fact]
    public void Parse_LinksMixedContentInOrder()
    {
        var segments = YouTubeRichText.Parse("Start 1:23 https://example.com #tag @user end");

        var links = segments.Where(segment => segment.Link is not null).ToArray();
        Assert.Equal(
        [
            YouTubeRichTextKind.Timestamp,
            YouTubeRichTextKind.Url,
            YouTubeRichTextKind.Hashtag,
            YouTubeRichTextKind.Mention,
        ], links.Select(link => link.Link!.Kind));
        Assert.Equal("Start ", segments[0].Text);
        Assert.Equal(" end", segments[^1].Text);
    }

    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("1:23", 83)]
    [InlineData("1:02:03", 3723)]
    [InlineData("75:30", 4530)]
    public void TryParseTimestamp_AcceptsYouTubeFormats(string input, int expectedSeconds)
    {
        Assert.True(YouTubeRichText.TryParseTimestamp(input, out var timestamp));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timestamp);
    }

    [Theory]
    [InlineData("1:2")]
    [InlineData("1:60")]
    [InlineData("1:75:30")]
    [InlineData(":30")]
    [InlineData("nope")]
    public void TryParseTimestamp_RejectsInvalidFormats(string input)
    {
        Assert.False(YouTubeRichText.TryParseTimestamp(input, out _));
    }
}
