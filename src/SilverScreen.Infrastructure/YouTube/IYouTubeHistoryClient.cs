namespace SilverScreen.Infrastructure.YouTube;

public interface IYouTubeHistoryClient
{
    Task<HistoryFeedResult> GetHistoryAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default);
}
