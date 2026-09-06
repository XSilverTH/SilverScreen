using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Player.Views;

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
        return PlaybackBackends.IsEmbedded(backend)
            ? embeddedPlayer.PresentAsync(request)
            : externalMpvPlayback.PlayAsync(request);
    }

    /// <summary>
    ///     Plays through whichever backend is NOT currently configured, so "open in alternate
    ///     player" stays a single decision point beside <see cref="PlayAsync" />.
    /// </summary>
    public Task<string> PlayAlternateAsync(PlaybackRequest request)
    {
        var backend = preferencesService.GetPreferences().PlaybackBackend;
        var firstVideo = request.Videos.Length > 0 ? request.Videos[0] : null;
        Logger.Information("Routing alternate playback for video {VideoId} ({Title}) against backend {Backend}",
            firstVideo?.Id, firstVideo?.Title, backend);
        return PlaybackBackends.IsEmbedded(backend)
            ? externalMpvPlayback.PlayAsync(request)
            : embeddedPlayer.PresentAsync(request);
    }
}