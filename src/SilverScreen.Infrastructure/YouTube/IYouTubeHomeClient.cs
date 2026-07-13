namespace SilverScreen.Infrastructure.YouTube;

public interface IYouTubeHomeClient
{
    Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default);
}