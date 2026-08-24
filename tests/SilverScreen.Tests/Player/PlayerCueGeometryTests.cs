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

public class PlayerCueGeometryTests
{
    private const double ViewWidth = 1280;
    private const double ViewHeight = 720;

    [Fact]
    public void CommentsCue_WhenInactive_TriggersWithinTriggerDistance()
    {
        // Inside trigger zone (x <= 80, within vertical margin [80, 640])
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(50, 360, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(80, 360, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Outside trigger zone (x > 80)
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(81, 360, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(100, 360, ViewWidth, ViewHeight, isCurrentlyActive: false));
    }

    [Fact]
    public void CommentsCue_WhenActive_RetainsVisibilityWithinActiveDistance()
    {
        // Inside active hysteresis zone (x <= 140)
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(81, 360, ViewWidth, ViewHeight, isCurrentlyActive: true));
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(140, 360, ViewWidth, ViewHeight, isCurrentlyActive: true));

        // Outside active hysteresis zone (x > 140)
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(141, 360, ViewWidth, ViewHeight, isCurrentlyActive: true));
    }

    [Fact]
    public void CommentsCue_RespectsTopAndBottomCornerMargins()
    {
        // Within x range but in top margin (y < 80)
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(40, 50, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Within x range but in bottom margin (y > 640)
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(40, 680, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Exactly on edge bounds
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(40, 80, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.True(PlayerCueGeometry.IsCommentsCueActive(40, 640, ViewWidth, ViewHeight, isCurrentlyActive: false));
    }

    [Fact]
    public void InfoCue_WhenInactive_TriggersWithinTriggerDistance()
    {
        // Inside bottom trigger zone (y >= 640 -> distanceFromBottom <= 80, within horizontal margin [80, 1200])
        Assert.True(PlayerCueGeometry.IsInfoCueActive(640, 670, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.True(PlayerCueGeometry.IsInfoCueActive(640, 640, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Outside trigger zone (distanceFromBottom > 80 -> y < 640)
        Assert.False(PlayerCueGeometry.IsInfoCueActive(640, 639, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.False(PlayerCueGeometry.IsInfoCueActive(640, 500, ViewWidth, ViewHeight, isCurrentlyActive: false));
    }

    [Fact]
    public void InfoCue_WhenActive_RetainsVisibilityWithinActiveDistance()
    {
        // Inside active hysteresis zone (distanceFromBottom <= 140 -> y >= 580)
        Assert.True(PlayerCueGeometry.IsInfoCueActive(640, 639, ViewWidth, ViewHeight, isCurrentlyActive: true));
        Assert.True(PlayerCueGeometry.IsInfoCueActive(640, 580, ViewWidth, ViewHeight, isCurrentlyActive: true));

        // Outside active hysteresis zone (distanceFromBottom > 140 -> y < 580)
        Assert.False(PlayerCueGeometry.IsInfoCueActive(640, 579, ViewWidth, ViewHeight, isCurrentlyActive: true));
    }

    [Fact]
    public void InfoCue_RespectsLeftAndRightCornerMargins()
    {
        // Within y range but in left margin (x < 80)
        Assert.False(PlayerCueGeometry.IsInfoCueActive(50, 700, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Within y range but in right margin (x > 1200)
        Assert.False(PlayerCueGeometry.IsInfoCueActive(1250, 700, ViewWidth, ViewHeight, isCurrentlyActive: false));

        // Exactly on edge bounds
        Assert.True(PlayerCueGeometry.IsInfoCueActive(80, 700, ViewWidth, ViewHeight, isCurrentlyActive: false));
        Assert.True(PlayerCueGeometry.IsInfoCueActive(1200, 700, ViewWidth, ViewHeight, isCurrentlyActive: false));
    }

    [Fact]
    public void Cues_HandleInvalidDimensions()
    {
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(10, 10, 0, 0, isCurrentlyActive: true));
        Assert.False(PlayerCueGeometry.IsCommentsCueActive(10, 10, -100, 100, isCurrentlyActive: true));
        Assert.False(PlayerCueGeometry.IsInfoCueActive(10, 10, 0, 0, isCurrentlyActive: true));
        Assert.False(PlayerCueGeometry.IsInfoCueActive(10, 10, 100, -100, isCurrentlyActive: true));
    }

    [Fact]
    public void Cues_SymmetricBehaviorRelativeToEdge()
    {
        const double distanceFromEdge = 60; // Less than 80 trigger distance
        const double centerSpan = 360;

        // Comments cue at 60px from left edge
        var commentsActive = PlayerCueGeometry.IsCommentsCueActive(
            distanceFromEdge,
            centerSpan,
            ViewWidth,
            ViewHeight,
            isCurrentlyActive: false);

        // Info cue at 60px from bottom edge
        var infoActive = PlayerCueGeometry.IsInfoCueActive(
            centerSpan,
            ViewHeight - distanceFromEdge,
            ViewWidth,
            ViewHeight,
            isCurrentlyActive: false);

        Assert.True(commentsActive);
        Assert.True(infoActive);
    }
}
