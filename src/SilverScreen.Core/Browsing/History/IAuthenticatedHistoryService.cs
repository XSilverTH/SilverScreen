using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Core.Browsing.History;

/// <summary>Loads the signed-in account's watch history from YouTube.</summary>
public interface IAuthenticatedHistoryService
{
    Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default);

    Task<AuthenticatedHistoryResult> LoadNextPageAsync(CancellationToken cancellationToken = default);
}