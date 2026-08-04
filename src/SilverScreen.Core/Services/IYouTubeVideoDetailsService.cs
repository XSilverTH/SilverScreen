using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IYouTubeVideoDetailsService
{
    Task<YouTubeVideoDetailsResult> GetDetailsAsync(
        string videoId,
        CancellationToken cancellationToken = default);
}
