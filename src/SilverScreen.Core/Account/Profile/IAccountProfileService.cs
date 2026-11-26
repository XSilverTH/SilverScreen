namespace SilverScreen.Core.Account.Profile;

public interface IAccountProfileService
{
    AccountProfile? TryGetCachedProfile() => null;

    Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}