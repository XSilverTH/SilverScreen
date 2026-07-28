using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IAccountProfileService
{
    AccountProfile? GetCachedProfile();

    Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
}