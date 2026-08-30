namespace SilverScreen.Core.Browsing.Common;

/// <summary>
/// Represents playback state supplied by YouTube for the authenticated viewer.
/// </summary>
/// <param name="WatchedFraction">The display watch fraction, or <c>null</c> when unavailable.</param>
/// <param name="ResumePosition">The separately saved resume position, or <c>null</c> when no position was supplied.</param>
/// <param name="IsCompleted">Whether YouTube marks the video as watched/completed.</param>
public sealed record YouTubePlaybackProgress(
    double? WatchedFraction,
    TimeSpan? ResumePosition,
    bool IsCompleted)
{
    public bool HasProgress => WatchedFraction.HasValue;

    public bool HasResumePosition => ResumePosition.HasValue;
}
