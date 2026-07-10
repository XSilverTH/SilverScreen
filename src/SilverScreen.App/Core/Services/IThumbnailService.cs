using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IThumbnailService
{
    Task<ThumbnailResult?> GetThumbnailAsync(VideoSummary video, CancellationToken cancellationToken = default);

    Task<ThumbnailResult?> GetThumbnailAsync(string thumbnailUrl, CancellationToken cancellationToken = default);
}

public sealed record ThumbnailResult(string LocalPath, bool WasCacheHit);
