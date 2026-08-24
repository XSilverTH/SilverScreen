namespace SilverScreen.Core.Browsing.History;

/// <summary>Loads the signed-in account's watch history from YouTube.</summary>
public interface IAuthenticatedHistoryService
{
    Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default);

    Task<AuthenticatedHistoryResult> LoadNextPageAsync(CancellationToken cancellationToken = default);
}