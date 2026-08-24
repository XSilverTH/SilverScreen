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

/// <summary>Reports real playback progress to YouTube for recommendation and history signals.</summary>
public interface IYouTubePlaybackTelemetryService : IDisposable
{
    /// <summary>Begins a telemetry session for one player instance.</summary>
    IYouTubePlaybackTelemetrySession Start(PlaybackRequest request);
}

/// <summary>Accepts state changes from one player instance until that player stops.</summary>
public interface IYouTubePlaybackTelemetrySession : IDisposable
{
    void UpdateState(PlaybackPresenceState state);
}