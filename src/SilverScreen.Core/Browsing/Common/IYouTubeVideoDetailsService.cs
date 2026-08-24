namespace SilverScreen.Core.Browsing.Common;

public interface IYouTubeVideoDetailsService
{
    Task<YouTubeVideoDetailsResult> GetDetailsAsync(
        string videoId,
        CancellationToken cancellationToken = default);
}