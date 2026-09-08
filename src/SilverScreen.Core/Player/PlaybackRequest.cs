using System.Collections.Immutable;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

/// <summary>
///     Immutable snapshot of the queue taken at the moment playback starts: a copied
///     video list plus the start index into it. The copy is by value at construction
///     time, so later queue edits never mutate an in-flight request; starting playback
///     again takes a new snapshot. Sync is one-directional queue-to-request only:
///     a request never writes back into <c>IQueueService</c>. External-list callers
///     derive watch URLs (<see cref="BuildWatchUrl" />) plus the effective start index,
///     and an empty snapshot is a guidance status (<see cref="EmptyQueueMessage" />,
///     coordinator entry guard), never a throw.
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