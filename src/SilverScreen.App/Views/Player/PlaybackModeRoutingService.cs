using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Views.Player;

internal sealed class PlaybackModeRoutingService(
    IPreferencesService preferencesService,
    IPlaybackService externalMpvPlayback,
    IEmbeddedPlayerPresenter embeddedPlayer)
    : IPlaybackService
{
    public Task<string> PlayAsync(PlaybackRequest request)
    {
        return preferencesService.GetPreferences().PlaybackBackend == PlaybackBackends.EmbeddedPlayer
            ? embeddedPlayer.PresentAsync(request)
            : externalMpvPlayback.PlayAsync(request);
    }
}