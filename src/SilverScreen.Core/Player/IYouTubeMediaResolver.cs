using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

public interface IYouTubeMediaResolver
{
    Task<YouTubeMediaResolutionResult> ResolveMediaAsync(
        string videoId,
        string? preferredQuality = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<YouTubeVideoDetailsResult> GetVideoDetailsAsync(
        string videoId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetQualityTracksAsync(
        string videoId,
        CancellationToken cancellationToken = default);
}