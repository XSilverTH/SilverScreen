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

namespace SilverScreen.Core.Player;

/// <summary>Stores and publishes the furthest locally played position for videos.</summary>
public interface IWatchProgressService
{
    event EventHandler<WatchProgress>? ProgressChanged;

    double? GetFraction(string videoId);

    double? GetResumeFraction(string videoId);

    void Update(PlaybackRequest request, PlaybackPresenceState state);
}