namespace SilverScreen.Core.Account.Profile;

public interface IAccountProfileService
{
    Task<AccountProfileResult> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}