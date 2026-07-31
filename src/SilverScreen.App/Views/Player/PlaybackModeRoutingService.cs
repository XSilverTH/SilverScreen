using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Views.Player;

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
        Logger.Information("Routing playback for video {VideoId} ({Title}) using backend {Backend}", firstVideo?.Id, firstVideo?.Title, backend);
        return backend == PlaybackBackends.EmbeddedPlayer
            ? embeddedPlayer.PresentAsync(request)
            : externalMpvPlayback.PlayAsync(request);
    }
}