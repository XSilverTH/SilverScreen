namespace SilverScreen.Core.Browsing.Home;

public interface IAuthenticatedHomeFeedService
{
    Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default);
    Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default);
}