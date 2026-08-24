using SilverScreen.Player.Views;
using Serilog;
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

namespace SilverScreen.Player;

internal sealed class PlaybackModeRoutingService(
    IPreferencesService preferencesService,
    IPlaybackService externalMpvPlayback,
    IEmbeddedPlayerPresenter embeddedPlayer)
    : IPlaybackService
{
    private static readonly ILogger Logger = Log.ForContext<PlaybackModeRoutingService>();

    public Task<string> PlayAsync(PlaybackRequest request)
    {
        var backend = preferencesService.GetPreferences().PlaybackBackend;
        var firstVideo = request.Videos.Length > 0 ? request.Videos[0] : null;
        Logger.Information("Routing playback for video {VideoId} ({Title}) using backend {Backend}", firstVideo?.Id,
            firstVideo?.Title, backend);
        return backend == PlaybackBackends.EmbeddedPlayer
            ? embeddedPlayer.PresentAsync(request)
            : externalMpvPlayback.PlayAsync(request);
    }
}