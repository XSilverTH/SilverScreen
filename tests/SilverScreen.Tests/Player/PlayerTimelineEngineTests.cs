using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player.Controllers;

namespace SilverScreen.Tests.Player;

public class PlayerTimelineEngineTests
{
    [Fact]
    public void FormatTime_FormatsMinutesAndHoursCorrectly()
    {
        Assert.Equal("0:00", PlayerTimelineEngine.FormatTime(TimeSpan.Zero));
        Assert.Equal("0:05", PlayerTimelineEngine.FormatTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("1:30", PlayerTimelineEngine.FormatTime(TimeSpan.FromSeconds(90)));
        Assert.Equal("10:00", PlayerTimelineEngine.FormatTime(TimeSpan.FromMinutes(10)));
        Assert.Equal("1:00:00", PlayerTimelineEngine.FormatTime(TimeSpan.FromHours(1)));
        Assert.Equal("2:05:09",
            PlayerTimelineEngine.FormatTime(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void FormatDelta_FormatsPositiveAndNegativeOffsets()
    {
        Assert.Equal("+0:00", PlayerTimelineEngine.FormatDelta(TimeSpan.Zero));
        Assert.Equal("+0:15", PlayerTimelineEngine.FormatDelta(TimeSpan.FromSeconds(15)));
        Assert.Equal("-0:15", PlayerTimelineEngine.FormatDelta(TimeSpan.FromSeconds(-15)));
        Assert.Equal("+1:30", PlayerTimelineEngine.FormatDelta(TimeSpan.FromSeconds(90)));
        Assert.Equal("-1:30", PlayerTimelineEngine.FormatDelta(TimeSpan.FromSeconds(-90)));
        Assert.Equal("+1:02:03",
            PlayerTimelineEngine.FormatDelta(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) +
                                             TimeSpan.FromSeconds(3)));
        Assert.Equal("-1:02:03",
            PlayerTimelineEngine.FormatDelta(
                -(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3))));
    }

    [Fact]
    public void FormatDurationLabel_FormatsLiveAndNormalDurations()
    {
        Assert.Equal("Live", PlayerTimelineEngine.FormatDurationLabel(TimeSpan.Zero));
        Assert.Equal("Live", PlayerTimelineEngine.FormatDurationLabel(TimeSpan.FromSeconds(-10)));
        Assert.Equal("5:00", PlayerTimelineEngine.FormatDurationLabel(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void CalculateProgressFraction_ClampsCorrectly()
    {
        Assert.Equal(0.0, PlayerTimelineEngine.CalculateProgressFraction(TimeSpan.FromSeconds(50), TimeSpan.Zero));
        Assert.Equal(0.0,
            PlayerTimelineEngine.CalculateProgressFraction(TimeSpan.FromSeconds(-10), TimeSpan.FromSeconds(100)));
        Assert.Equal(0.5,
            PlayerTimelineEngine.CalculateProgressFraction(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100)));
        Assert.Equal(1.0,
            PlayerTimelineEngine.CalculateProgressFraction(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void CalculateScrubCueBadgePosition_CentersAndClampsToMargins()
    {
        const double hostWidth = 1000;
        const double cueWidth = 100;

        // Near left edge -> clamped to margin (8)
        Assert.Equal(8, PlayerTimelineEngine.CalculateScrubCueBadgePosition(20, cueWidth, hostWidth));

        // Center -> pointerX - cueWidth/2 (500 - 50 = 450)
        Assert.Equal(450, PlayerTimelineEngine.CalculateScrubCueBadgePosition(500, cueWidth, hostWidth));

        // Near right edge -> clamped to hostWidth - cueWidth - 8 = 892
        Assert.Equal(892, PlayerTimelineEngine.CalculateScrubCueBadgePosition(980, cueWidth, hostWidth));

        // Zero cue width defaults to 80px width
        Assert.Equal(460, PlayerTimelineEngine.CalculateScrubCueBadgePosition(500, 0, hostWidth));
    }

    [Fact]
    public void GetChapterAt_FindsCorrectChapterByTimestamp()
    {
        var chapters = new List<LibMpvChapter>
        {
            new(TimeSpan.Zero, "Intro"),
            new(TimeSpan.FromMinutes(1), "Chapter 1"),
            new(TimeSpan.FromMinutes(5), "Chapter 2"),
            new(TimeSpan.FromMinutes(10), "Outro")
        };

        Assert.Null(PlayerTimelineEngine.GetChapterAt(TimeSpan.FromSeconds(10), []));
        Assert.Equal("Intro", PlayerTimelineEngine.GetChapterAt(TimeSpan.Zero, chapters)?.Title);
        Assert.Equal("Intro", PlayerTimelineEngine.GetChapterAt(TimeSpan.FromSeconds(30), chapters)?.Title);
        Assert.Equal("Chapter 1", PlayerTimelineEngine.GetChapterAt(TimeSpan.FromMinutes(1), chapters)?.Title);
        Assert.Equal("Chapter 1", PlayerTimelineEngine.GetChapterAt(TimeSpan.FromMinutes(3), chapters)?.Title);
        Assert.Equal("Chapter 2", PlayerTimelineEngine.GetChapterAt(TimeSpan.FromMinutes(5), chapters)?.Title);
        Assert.Equal("Outro", PlayerTimelineEngine.GetChapterAt(TimeSpan.FromMinutes(15), chapters)?.Title);
    }

    [Fact]
    public void CalculateChapterMarkerPosition_ComputesAndClampsCoordinates()
    {
        var duration = TimeSpan.FromSeconds(100);
        const int trackStart = 10;
        const int trackWidth = 500;
        const int hostWidth = 600;

        // At 0% -> trackPos = 10, markerX = 10 - 10 = 0
        Assert.Equal(0,
            PlayerTimelineEngine.CalculateChapterMarkerPosition(TimeSpan.Zero, duration, trackStart, trackWidth,
                hostWidth));

        // At 50% -> trackPos = 260, markerX = 260 - 10 = 250
        Assert.Equal(250,
            PlayerTimelineEngine.CalculateChapterMarkerPosition(TimeSpan.FromSeconds(50), duration, trackStart,
                trackWidth, hostWidth));

        // At 100% -> trackPos = 510, markerX = 510 - 10 = 500
        Assert.Equal(500,
            PlayerTimelineEngine.CalculateChapterMarkerPosition(TimeSpan.FromSeconds(100), duration, trackStart,
                trackWidth, hostWidth));
    }

    [Fact]
    public void ScrubbingLifecycle_TracksStateAndDeltas()
    {
        var engine = new PlayerTimelineEngine();
        engine.SetPositionDirect(TimeSpan.FromSeconds(60));
        engine.SetDuration(TimeSpan.FromSeconds(300));

        Assert.False(engine.IsScrubbing);
        Assert.Equal(TimeSpan.FromSeconds(60), engine.PlaybackPosition);

        // Begin scrub
        engine.BeginScrub(60);
        Assert.True(engine.IsScrubbing);
        Assert.Equal(TimeSpan.FromSeconds(60), engine.ScrubStartPosition);
        Assert.Equal(60, engine.LatestScrubPositionSeconds);

        // Delta calculation
        var deltaForward = engine.CalculateScrubDelta(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(30), deltaForward);

        var deltaBackward = engine.CalculateScrubDelta(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(-40), deltaBackward);

        // Update scrub position
        engine.UpdateScrub(120);
        Assert.Equal(120, engine.LatestScrubPositionSeconds);

        // Cancel scrub restores start position
        var restored = engine.CancelScrub();
        Assert.False(engine.IsScrubbing);
        Assert.Equal(TimeSpan.FromSeconds(60), restored);
        Assert.Equal(TimeSpan.FromSeconds(60), engine.PlaybackPosition);

        // Begin and End scrub
        engine.BeginScrub(60);
        var finalSeek = engine.EndScrub(150);
        Assert.False(engine.IsScrubbing);
        Assert.Equal(150, finalSeek);
        Assert.Equal(TimeSpan.FromSeconds(150), engine.PlaybackPosition);
        Assert.Equal(150, engine.PendingSeekTargetSeconds);
    }

    [Fact]
    public void SeekingReconciliation_LatchesIncomingUpdatesCorrectly()
    {
        long currentTime = 1000;
        var engine = new PlayerTimelineEngine(
            120,
            400,
            1.5,
            () => currentTime);

        // Initial state
        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), [],
            out var initialAccepted);
        Assert.True(initialAccepted);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.PlaybackPosition);

        // User performs a seek to 50s at t = 1000
        engine.RegisterSeek(50);
        Assert.Equal(50, engine.PendingSeekTargetSeconds);
        Assert.Equal(1400, engine.ReconciliationLatchExpiry);

        // Backend reports stale playback position 11s at t = 1100 (within latch, far from 50s)
        currentTime = 1100;
        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(100), [],
            out var staleAccepted);
        Assert.False(staleAccepted);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.PlaybackPosition); // Stale position ignored

        // Backend reports position 49.2s at t = 1200 (within latch, close to 50s within 1.5s tolerance)
        currentTime = 1200;
        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(49.2), TimeSpan.FromSeconds(100), [],
            out var reconciledAccepted);
        Assert.True(reconciledAccepted);
        Assert.Equal(TimeSpan.FromSeconds(49.2), engine.PlaybackPosition);
        Assert.Equal(-1, engine.PendingSeekTargetSeconds); // Latch cleared
        Assert.Equal(0, engine.ReconciliationLatchExpiry);

        // New seek to 80s at t = 2000
        currentTime = 2000;
        engine.RegisterSeek(80);

        // After latch expires at t = 2500, un-reconciled position 70s is accepted
        currentTime = 2500;
        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(100), [],
            out var expiredAccepted);
        Assert.True(expiredAccepted);
        Assert.Equal(TimeSpan.FromSeconds(70), engine.PlaybackPosition);
    }

    [Fact]
    public void SeekingReconciliation_IgnoresUpdatesWhileScrubbing()
    {
        var engine = new PlayerTimelineEngine();
        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), [], out _);

        engine.BeginScrub(10);

        engine.UpdatePlaybackState(true, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(100), [], out var accepted);
        Assert.False(accepted);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.PlaybackPosition);
    }

    [Fact]
    public void SeekThrottling_EvaluatesIntervalAndDelays()
    {
        long currentTime = 1000;
        var engine = new PlayerTimelineEngine(tickCountProvider: () => currentTime);

        // First seek after start -> elapsed is infinite / >= 120 -> immediately allowed
        Assert.True(engine.ShouldDispatchThrottledSeek(out var delay1));
        Assert.Equal(0u, delay1);
        Assert.Equal(1000, engine.LastThrottledSeekTime);

        // 30ms later -> elapsed = 30 < 120 -> throttle required with remaining delay (90ms)
        currentTime = 1030;
        Assert.False(engine.ShouldDispatchThrottledSeek(out var delay2));
        Assert.Equal(90u, delay2);

        // 120ms later (t = 1150) -> elapsed = 150 >= 120 -> immediately allowed
        currentTime = 1150;
        Assert.True(engine.ShouldDispatchThrottledSeek(out var delay3));
        Assert.Equal(0u, delay3);
        Assert.Equal(1150, engine.LastThrottledSeekTime);
    }

    [Fact]
    public void SponsorBlock_AutoSkipEvaluation()
    {
        var segments = new List<SponsorBlockSegment>
        {
            new("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), SponsorBlockCategories.Sponsor),
            new("seg2", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90), SponsorBlockCategories.Intro)
        };
        var skippedIds = new HashSet<string>(StringComparer.Ordinal);

        // Normal playback outside segment -> false
        Assert.False(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(5), segments, false, true, skippedIds,
            out var s1));
        Assert.Null(s1);

        // Normal playback inside seg1 -> true and returns seg1
        Assert.True(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(15), segments, false, true, skippedIds,
            out var s2));
        Assert.NotNull(s2);
        Assert.Equal("seg1", s2!.Id);
        Assert.Contains("seg1", skippedIds);

        // Already skipped seg1 -> false
        Assert.False(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(15), segments, false, true, skippedIds,
            out var s3));
        Assert.Null(s3);

        // Inside seg2 but paused -> false
        Assert.False(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(65), segments, true, true, skippedIds,
            out var s4));
        Assert.Null(s4);

        // Inside seg2 but auto-skip disabled -> false
        Assert.False(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(65), segments, false, false, skippedIds,
            out var s5));
        Assert.Null(s5);

        // Inside seg2, unpaused and enabled -> true
        Assert.True(PlayerTimelineEngine.ShouldAutoSkip(TimeSpan.FromSeconds(65), segments, false, true, skippedIds,
            out var s6));
        Assert.NotNull(s6);
        Assert.Equal("seg2", s6!.Id);
    }

    [Fact]
    public void SponsorBlock_ManualPromptEvaluation()
    {
        var segment = new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
            SponsorBlockCategories.Sponsor);

        // Entering new segment -> show
        Assert.True(PlayerTimelineEngine.ShouldShowManualPrompt(null, segment, false, false, false));

        // Staying in same segment playing -> do not re-show
        Assert.False(PlayerTimelineEngine.ShouldShowManualPrompt(segment, segment, false, false, false));

        // Seeking into same segment -> show
        Assert.True(PlayerTimelineEngine.ShouldShowManualPrompt(segment, segment, false, false, true));

        // Pausing inside segment -> show
        Assert.True(PlayerTimelineEngine.ShouldShowManualPrompt(segment, segment, true, false, false));

        // Candidate null -> false
        Assert.False(PlayerTimelineEngine.ShouldShowManualPrompt(segment, null, false, false, false));
    }

    [Fact]
    public void SponsorBlock_LabelsAndConfigKeys()
    {
        Assert.Equal("Sponsor", PlayerTimelineEngine.GetSponsorBlockCategoryLabel(SponsorBlockCategories.Sponsor));
        Assert.Equal("Self-promotion",
            PlayerTimelineEngine.GetSponsorBlockCategoryLabel(SponsorBlockCategories.SelfPromotion));
        Assert.Equal("Intro", PlayerTimelineEngine.GetSponsorBlockCategoryLabel(SponsorBlockCategories.Intro));
        Assert.Equal("Custom", PlayerTimelineEngine.GetSponsorBlockCategoryLabel("Custom"));

        Assert.Equal("player-sponsorblock-skip-button-sponsor",
            PlayerTimelineEngine.GetSponsorBlockButtonColorClass(SponsorBlockCategories.Sponsor));
        Assert.Equal("player-sponsorblock-skip-button-intro",
            PlayerTimelineEngine.GetSponsorBlockButtonColorClass(SponsorBlockCategories.Intro));
        Assert.Equal("player-sponsorblock-skip-button-sponsor",
            PlayerTimelineEngine.GetSponsorBlockButtonColorClass("unknown"));

        Assert.Equal("disabled",
            PlayerTimelineEngine.GetSponsorBlockConfigurationKey(false, false, [SponsorBlockCategories.Sponsor]));
        Assert.Equal("True:True:sponsor,intro",
            PlayerTimelineEngine.GetSponsorBlockConfigurationKey(true, true,
                [SponsorBlockCategories.Sponsor, SponsorBlockCategories.Intro]));
    }

    [Fact]
    public void ResumeEvaluation_UsesYouTubeResumePositionAndCompletion()
    {
        var duration = TimeSpan.FromMinutes(10);

        Assert.False(PlayerTimelineEngine.TryGetResumePosition(null, duration, out _));
        Assert.False(PlayerTimelineEngine.TryGetResumePosition(
            new YouTubePlaybackProgress(0.5, null, false), duration, out _));
        Assert.False(PlayerTimelineEngine.TryGetResumePosition(
            new YouTubePlaybackProgress(1, TimeSpan.FromSeconds(300), true), duration, out _));
        Assert.False(PlayerTimelineEngine.TryGetResumePosition(
            new YouTubePlaybackProgress(null, TimeSpan.Zero, false), duration, out _));

        var progress = new YouTubePlaybackProgress(0.5, TimeSpan.FromSeconds(73), false);
        Assert.True(PlayerTimelineEngine.TryGetResumePosition(progress, duration, out var position));
        Assert.Equal(TimeSpan.FromSeconds(73), position);

        Assert.Equal(ResumePromptState.None,
            PlayerTimelineEngine.GetResumePromptState(null, duration, true, true, out _));
        Assert.Equal(ResumePromptState.AutoResume,
            PlayerTimelineEngine.GetResumePromptState(progress, duration, true, false, out var autoPosition));
        Assert.Equal(TimeSpan.FromSeconds(73), autoPosition);
        Assert.Equal(ResumePromptState.ManualResume,
            PlayerTimelineEngine.GetResumePromptState(progress, duration, false, true, out var manualPosition));
        Assert.Equal(TimeSpan.FromSeconds(73), manualPosition);
        Assert.Equal(ResumePromptState.None,
            PlayerTimelineEngine.GetResumePromptState(progress, duration, false, false, out _));
    }

    [Fact]
    public void Reset_ClearsAllEngineState()
    {
        var engine = new PlayerTimelineEngine();
        engine.SetPositionDirect(TimeSpan.FromSeconds(120));
        engine.SetDuration(TimeSpan.FromSeconds(600));
        engine.SetChapters([new LibMpvChapter(TimeSpan.Zero, "Ch1")]);
        engine.BeginScrub(120);
        engine.RegisterSeek(200);

        engine.Reset();

        Assert.False(engine.IsScrubbing);
        Assert.Equal(TimeSpan.Zero, engine.PlaybackPosition);
        Assert.Equal(TimeSpan.Zero, engine.Duration);
        Assert.Empty(engine.Chapters);
        Assert.False(engine.HasMedia);
        Assert.Equal(TimeSpan.Zero, engine.ScrubStartPosition);
        Assert.Equal(0, engine.LatestScrubPositionSeconds);
        Assert.Equal(-1, engine.PendingSeekTargetSeconds);
        Assert.Equal(0, engine.ReconciliationLatchExpiry);
        Assert.Equal(0, engine.LastThrottledSeekTime);
    }
}