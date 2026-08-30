using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

/// <summary>Reads the current viewer playback state directly from YouTube.</summary>
public interface IYouTubePlaybackProgressService
{
    /// <summary>
    /// Gets YouTube's current playback state for a video. A null result means that YouTube did not provide
    /// viewer-specific progress, including for unauthenticated or unavailable sessions.
    /// </summary>
    Task<YouTubePlaybackProgress?> GetAsync(string videoId, CancellationToken cancellationToken = default);
}
