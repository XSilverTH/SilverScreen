using SilverScreen.Views.Components;

namespace SilverScreen.Tests;

public sealed class VideoCardViewTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "1 day ago")]
    [InlineData(2, "2 days ago")]
    [InlineData(6, "6 days ago")]
    [InlineData(7, "1 week ago")]
    [InlineData(29, "4 weeks ago")]
    [InlineData(30, "1 month ago")]
    [InlineData(364, "12 months ago")]
    [InlineData(365, "1 year ago")]
    [InlineData(730, "2 years ago")]
    public void FormatUploadAge_FormatsRelativeAgeBoundaries(int elapsedDays, string expected)
    {
        var uploadDate = Today.AddDays(-elapsedDays);

        Assert.Equal(expected, VideoCardView.FormatUploadAge(uploadDate, Today));
    }

    [Fact]
    public void FormatUploadAge_ClampsFutureDatesToToday()
    {
        Assert.Equal("Today", VideoCardView.FormatUploadAge(Today.AddDays(1), Today));
    }

    [Theory]
    [InlineData(-1, "Just now")]
    [InlineData(0, "Just now")]
    [InlineData(59, "Just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(300, "5 minutes ago")]
    [InlineData(3_600, "1 hour ago")]
    [InlineData(7_200, "2 hours ago")]
    public void FormatUploadAge_FormatsRecentPublishTimes(int elapsedSeconds, string expected)
    {
        var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, VideoCardView.FormatUploadAge(now.AddSeconds(-elapsedSeconds), now));
    }
}
