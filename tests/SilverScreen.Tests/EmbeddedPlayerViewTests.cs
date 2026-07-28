using SilverScreen.Core.Models;
using SilverScreen.Views.Player;

namespace SilverScreen.Tests;

public sealed class EmbeddedPlayerViewTests
{
    [Fact]
    public void FindSponsorBlockSegmentAtPosition_IncludesStartAndExcludesEnd()
    {
        var sponsor = new SponsorBlockSegment("sponsor", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20),
            SponsorBlockCategories.Sponsor);
        var outro = new SponsorBlockSegment("outro", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40),
            SponsorBlockCategories.Outro);
        IReadOnlyList<SponsorBlockSegment> segments = [sponsor, outro];

        Assert.Same(sponsor, EmbeddedPlayerView.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(10)));
        Assert.Same(sponsor, EmbeddedPlayerView.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(19.999)));
        Assert.Null(EmbeddedPlayerView.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(20)));
        Assert.Same(outro, EmbeddedPlayerView.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(300, 786)]
    [InlineData(600, 1548)]
    public void GetTimelineTrackPosition_UsesTheScalesActualTrackBounds(double positionSeconds,
        double expectedCoordinate)
    {
        var coordinate = EmbeddedPlayerView.GetTimelineTrackPosition(TimeSpan.FromSeconds(positionSeconds),
            TimeSpan.FromMinutes(10), 24, 1524);

        Assert.Equal(expectedCoordinate, coordinate);
    }

    [Fact]
    public void GetTimelineTrackBounds_AnchorsTheActivePlaybackPositionToTheRenderedThumb()
    {
        var (trackStart, trackWidth) = EmbeddedPlayerView.GetTimelineTrackBounds(22, 370, 159, 205,
            TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600));

        Assert.Equal(20, trackStart);
        Assert.Equal(324, trackWidth);
        Assert.Equal(182, EmbeddedPlayerView.GetTimelineTrackPosition(TimeSpan.FromSeconds(300),
            TimeSpan.FromSeconds(600), trackStart, trackWidth));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ManualSponsorBlockSkipEnabled_RequiresTimelineDisplayWithoutAutoSkip(bool displayEnabled,
        bool autoSkipEnabled, bool expected)
    {
        var preferences = new AppPreferences
        {
            SponsorBlockSegmentDisplayEnabled = displayEnabled,
            SponsorBlockAutoSkipEnabled = autoSkipEnabled
        };

        Assert.Equal(expected, EmbeddedPlayerView.ManualSponsorBlockSkipEnabled(preferences));
    }
}
