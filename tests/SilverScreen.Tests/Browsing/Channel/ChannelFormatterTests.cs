using SilverScreen.Browsing.Channel;

namespace SilverScreen.Tests.Browsing.Channel;

public class ChannelFormatterTests
{
    [Theory]
    [InlineData("https://www.youtube.com/@mkbhd", "@mkbhd")]
    [InlineData("https://www.youtube.com/@mkbhd/videos", "@mkbhd")]
    [InlineData("https://www.youtube.com/@veritasium/", "@veritasium")]
    [InlineData("https://www.youtube.com/channel/UC12345", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ExtractHandle_ExtractsCorrectly(string? url, string? expected)
    {
        var result = ChannelFormatter.ExtractHandle(url);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, "0 subscribers")]
    [InlineData(1, "1 subscriber")]
    [InlineData(42, "42 subscribers")]
    [InlineData(1_500, "1.5K subscribers")]
    [InlineData(25_000, "25K subscribers")]
    [InlineData(3_400_000, "3.4M subscribers")]
    public void FormatSubscribers_FormatsCorrectly(long count, string expected)
    {
        var result = ChannelFormatter.FormatSubscribers(count);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractContactInfo_FiltersMatchingLines()
    {
        var description = "Welcome to my channel!\nFor business inquir: biz@example.com\nFollow me on Twitter\nContact: 555-1234";
        var result = ChannelFormatter.ExtractContactInfo(description);
        Assert.NotNull(result);
        Assert.Contains("biz@example.com", result);
        Assert.Contains("Contact: 555-1234", result);
        Assert.DoesNotContain("Follow me on Twitter", result);
    }

    [Fact]
    public void ExtractContactInfo_WithNoContactLines_ReturnsNull()
    {
        var description = "Just playing video games and having fun.";
        var result = ChannelFormatter.ExtractContactInfo(description);
        Assert.Null(result);
    }
}
