using System.Collections.Immutable;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

/// <summary>
/// Immutable snapshot of the queue taken at the moment playback starts.
/// Sync is one-directional: the queue service owns the live list, and a request
/// freezes (video list + start index) at call time. Later queue edits never
/// mutate an in-flight request; starting playback again takes a new snapshot.
/// </summary>
public sealed record PlaybackRequest(ImmutableArray<VideoSummary> Videos, int StartIndex = 0)
{
    /// <summary>User-facing guidance returned when there is nothing to play. Never thrown.</summary>
    public const string EmptyQueueMessage = "Add a video to the queue to start playback.";

    /// <summary>Start position clamped into the snapshot. 0 when the snapshot is empty.</summary>
    public int EffectiveStartIndex => Videos.IsDefaultOrEmpty ? 0 : Math.Clamp(StartIndex, 0, Videos.Length - 1);

    public static string? BuildWatchUrl(string videoId)
    {
        return LooksLikeYouTubeVideoId(videoId)
            ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}"
            : null;
    }

    public static bool LooksLikeYouTubeVideoId(string id)
    {
        return id.Length == 11
               && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}