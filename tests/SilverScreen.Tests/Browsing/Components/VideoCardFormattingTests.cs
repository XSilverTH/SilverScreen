using SilverScreen.Browsing.Components;
using SilverScreen.Core.Domain;

namespace SilverScreen.Tests.Browsing.Components;

public class VideoCardFormattingTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(45, "0:45")]
    [InlineData(75, "1:15")]
    [InlineData(3665, "1:01:05")]
    public void FormatDuration_FormatsCorrectly(int totalSeconds, string expected)
    {
        var result = VideoCardFormatting.FormatDuration(TimeSpan.FromSeconds(totalSeconds));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatUploadAge_DateOnly_CalculatesDaysWeeksMonthsYears()
    {
        var today = new DateOnly(2026, 11, 21);
        Assert.Equal("Today", VideoCardFormatting.FormatUploadAge(today, today));
        Assert.Equal("1 day ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-1), today));
        Assert.Equal("3 days ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-3), today));
        Assert.Equal("2 weeks ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-14), today));
        Assert.Equal("2 months ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-60), today));
        Assert.Equal("1 year ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-365), today));
        Assert.Equal("2 years ago", VideoCardFormatting.FormatUploadAge(today.AddDays(-730), today));
    }

    [Fact]
    public void FormatUploadAge_DateTimeOffset_CalculatesMinutesHours()
    {
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Just now", VideoCardFormatting.FormatUploadAge(now.AddSeconds(-30), now));
        Assert.Equal("15 minutes ago", VideoCardFormatting.FormatUploadAge(now.AddMinutes(-15), now));
        Assert.Equal("3 hours ago", VideoCardFormatting.FormatUploadAge(now.AddHours(-3), now));
    }

    [Fact]
    public void FormatCardTooltip_IncludesTitleAndChannel()
    {
        var video = new VideoSummary(
            "v1", "Test Title", "Test Channel", TimeSpan.FromMinutes(5), "http://thumb", false);
        var tooltip = VideoCardFormatting.FormatCardTooltip(video);
        Assert.Contains("Test Title", tooltip);
        Assert.Contains("Test Channel", tooltip);
    }
}
