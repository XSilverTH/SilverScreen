using Gtk;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Timeline;

/// <summary>
///     Encapsulates the timeline scale, drag scrubbing cue, chapter markers, and time display labels.
/// </summary>
public partial class PlayerTimelineView : ViewBase<Box>
{
    private PlayerChapterOverlay? _chapterOverlay;
    private PlayerTimelineController? _timelineController;

    public Scale TimelineScale => player_timeline;
    public Overlay TimelineOverlay => player_timeline_overlay;
    public bool IsScrubbing => _timelineController?.IsScrubbing ?? false;

    public void Attach(LibMpvPlayer player, Action registerActivity)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(registerActivity);

        _timelineController = Lifetime.Own(new PlayerTimelineController(
            player_timeline,
            player_timeline_overlay,
            player_scrub_cue,
            player_scrub_time_label,
            player_scrub_delta_label,
            player_scrub_chapter_label,
            player_position_label,
            player_duration_label,
            player.SeekAbsolute,
            registerActivity));

        _chapterOverlay = Lifetime.Own(new PlayerChapterOverlay(
            player_timeline_overlay,
            player_timeline,
            () => _timelineController.PlaybackPosition,
            pos => player.SeekAbsolute(pos),
            registerActivity));
    }

    public void UpdatePosition(LibMpvPlaybackState state, bool updateWidgets = true)
    {
        _timelineController?.UpdatePosition(state, updateWidgets);
    }

    public void UpdateChapters(IReadOnlyList<LibMpvChapter> chapters, TimeSpan duration, bool layoutNow = true)
    {
        _chapterOverlay?.Update(chapters, duration, layoutNow);
    }

    public void SetExpectedMedia(TimeSpan duration, bool isLive)
    {
        _timelineController?.SetExpectedMedia(duration, isLive);
    }

    public void Reset()
    {
        _timelineController?.Reset();
        _chapterOverlay?.Update([], TimeSpan.Zero);
    }

    public void LayoutChapters()
    {
        _chapterOverlay?.Layout();
    }

    public void SeekAbsolute(double position, bool exact = true)
    {
        _timelineController?.SeekAbsolute(position, exact);
    }

    public void CancelScrubbing()
    {
        _timelineController?.CancelScrubbing();
    }
}