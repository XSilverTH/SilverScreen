using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpVideoDetailsService(IYouTubeMediaResolver mediaResolver) : IYouTubeVideoDetailsService
{
    private readonly IYouTubeMediaResolver _mediaResolver = mediaResolver ?? throw new ArgumentNullException(nameof(mediaResolver));

    public Task<YouTubeVideoDetailsResult> GetDetailsAsync(string videoId, CancellationToken cancellationToken = default)
    {
        return _mediaResolver.GetVideoDetailsAsync(videoId, forceRefresh: false, cancellationToken);
    }
}
