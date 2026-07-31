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

        Assert.Same(sponsor,
            PlayerSponsorBlockController.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(10)));
        Assert.Same(sponsor,
            PlayerSponsorBlockController.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(19.999)));
        Assert.Null(PlayerSponsorBlockController.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(20)));
        Assert.Same(outro,
            PlayerSponsorBlockController.FindSponsorBlockSegmentAtPosition(segments, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(300, 786)]
    [InlineData(600, 1548)]
    public void GetTimelineTrackPosition_UsesTheScalesActualTrackBounds(double positionSeconds,
        double expectedCoordinate)
    {
        var coordinate = PlayerTimelineGeometry.GetTrackPosition(TimeSpan.FromSeconds(positionSeconds),
            TimeSpan.FromMinutes(10), 24, 1524);

        Assert.Equal(expectedCoordinate, coordinate);
    }

    [Fact]
    public void GetTimelineTrackBounds_AnchorsTheActivePlaybackPositionToTheRenderedThumb()
    {
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrackBounds(22, 370, 159, 205,
            TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600));

        Assert.Equal(20, trackStart);
        Assert.Equal(324, trackWidth);
        Assert.Equal(182, PlayerTimelineGeometry.GetTrackPosition(TimeSpan.FromSeconds(300),
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

        Assert.Equal(expected, PlayerSponsorBlockController.ManualSponsorBlockSkipEnabled(preferences));
    }

    [Theory]
    [InlineData(0.25, 240, 60)]
    [InlineData(0.5, 7, 0)]
    public void TryGetResumePosition_RequiresASubstantialSavedPosition(double fraction, double durationSeconds,
        double expectedPositionSeconds)
    {
        var canResume = PlayerResumeController.TryGetResumePosition(fraction, TimeSpan.FromSeconds(durationSeconds),
            out var position);

        Assert.Equal(expectedPositionSeconds > 0, canResume);
        Assert.Equal(expectedPositionSeconds, position.TotalSeconds);
    }
}