namespace SilverScreen.Core.Account.Profile;

public interface IAccountProfileService
{
    AccountProfile? TryGetCachedProfile()
    {
        return null;
    }

    Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}