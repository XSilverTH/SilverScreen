namespace SilverScreen.Core.Account.Profile;

public interface IAccountProfileService
{
    AccountProfile? GetCachedProfile();

    Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}