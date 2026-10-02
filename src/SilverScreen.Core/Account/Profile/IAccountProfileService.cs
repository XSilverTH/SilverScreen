namespace SilverScreen.Core.Account.Profile;

public interface IAccountProfileService
{
    AccountProfile? GetCachedProfile();

    Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}