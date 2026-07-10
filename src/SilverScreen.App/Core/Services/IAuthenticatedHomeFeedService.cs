using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IAuthenticatedHomeFeedService : IFeedService
{
    Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default);
    Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default);
}