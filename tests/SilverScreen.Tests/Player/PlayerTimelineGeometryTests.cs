using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


namespace SilverScreen.Tests.Player;

public class PlayerTimelineGeometryTests
{
    [Fact]
    public void GetTrackBoundsReturnsCenterWhenDurationIsZero()
    {
        var (start, width) = PlayerTimelineGeometry.GetTrackBounds(
            10,
            500,
            10,
            30,
            TimeSpan.Zero,
            TimeSpan.Zero);

        Assert.Equal(20, start);
        Assert.Equal(480, width);
    }

    [Fact]
    public void GetTrackBoundsCalculatesCorrectTrackPosition()
    {
        var duration = TimeSpan.FromMinutes(10);
        var position = TimeSpan.FromMinutes(5); // 50%

        // Trough from 0 to 500, slider is 20px wide centered at 250 (from 240 to 260)
        var (start, width) = PlayerTimelineGeometry.GetTrackBounds(
            0,
            500,
            240,
            260,
            position,
            duration);

        Assert.Equal(480, width);
        // Slider center is 250, current fraction is 0.5, track width is 480 -> 250 - 0.5 * 480 = 10
        Assert.Equal(10, start);
    }

    [Fact]
    public void GetTrackPositionMapsTimeSpanToPixels()
    {
        var duration = TimeSpan.FromSeconds(100);
        const int trackStart = 10;
        const int trackWidth = 500;

        Assert.Equal(10, PlayerTimelineGeometry.GetTrackPosition(TimeSpan.Zero, duration, trackStart, trackWidth));
        Assert.Equal(260,
            PlayerTimelineGeometry.GetTrackPosition(TimeSpan.FromSeconds(50), duration, trackStart, trackWidth));
        Assert.Equal(510,
            PlayerTimelineGeometry.GetTrackPosition(TimeSpan.FromSeconds(100), duration, trackStart, trackWidth));
        Assert.Equal(510,
            PlayerTimelineGeometry.GetTrackPosition(TimeSpan.FromSeconds(150), duration, trackStart,
                trackWidth)); // Clamped
    }

    [Fact]
    public void GetPositionAtCoordinateMapsPixelsToTimeSpan()
    {
        var duration = TimeSpan.FromSeconds(100);
        const int trackStart = 10;
        const int trackWidth = 500;

        // Before start -> clamped to 0
        Assert.Equal(TimeSpan.Zero,
            PlayerTimelineGeometry.GetPositionAtCoordinate(5, trackStart, trackWidth, duration));
        Assert.Equal(TimeSpan.Zero,
            PlayerTimelineGeometry.GetPositionAtCoordinate(10, trackStart, trackWidth, duration));

        // Midpoint
        Assert.Equal(TimeSpan.FromSeconds(50),
            PlayerTimelineGeometry.GetPositionAtCoordinate(260, trackStart, trackWidth, duration));

        // End
        Assert.Equal(TimeSpan.FromSeconds(100),
            PlayerTimelineGeometry.GetPositionAtCoordinate(510, trackStart, trackWidth, duration));

        // Past end -> clamped to duration
        Assert.Equal(TimeSpan.FromSeconds(100),
            PlayerTimelineGeometry.GetPositionAtCoordinate(600, trackStart, trackWidth, duration));
    }

    [Fact]
    public void GetPositionAtCoordinateHandlesZeroDurationOrZeroWidth()
    {
        Assert.Equal(TimeSpan.Zero,
            PlayerTimelineGeometry.GetPositionAtCoordinate(100, 10, 0, TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.Zero, PlayerTimelineGeometry.GetPositionAtCoordinate(100, 10, 500, TimeSpan.Zero));
    }
}
