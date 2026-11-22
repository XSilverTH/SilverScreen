using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Player;

internal sealed class PlaybackModeRoutingService(
    IPreferencesService preferencesService,
    IPlaybackService externalMpvPlayback,
    IEmbeddedPlayerPresenter embeddedPlayer)
    : IPlaybackModeRoutingService
{
    private static readonly ILogger Logger = Log.ForContext<PlaybackModeRoutingService>();
    public bool HasMedia => embeddedPlayer.HasMedia;
    public bool IsPaused => embeddedPlayer.IsPaused;

    public Task<string> PlayAsync(PlaybackRequest request)
    {
        var backend = preferencesService.GetPreferences().PlaybackBackendKind;
        Logger.Debug("Starting playback for video using {Backend}", backend);
        return PlaybackBackends.IsEmbedded(backend)
            ? embeddedPlayer.PresentAsync(request)
            : externalMpvPlayback.PlayAsync(request);
    }

    public event EventHandler? PlaybackStateChanged
    {
        add => embeddedPlayer.PlaybackStateChanged += value;
        remove => embeddedPlayer.PlaybackStateChanged -= value;
    }

    public Task TogglePauseAsync()
    {
        return embeddedPlayer.TogglePauseAsync();
    }

    /// <summary>
    ///     Plays through whichever backend is NOT currently configured, so "open in alternate
    ///     player" stays a single decision point beside <see cref="PlayAsync" />.
    /// </summary>
    public Task<string> PlayAlternateAsync(PlaybackRequest request)
    {
        var backend = preferencesService.GetPreferences().PlaybackBackendKind;
        Logger.Debug("Starting playback for video with alternate {Backend}", backend);
        return PlaybackBackends.IsEmbedded(backend)
            ? externalMpvPlayback.PlayAsync(request)
            : embeddedPlayer.PresentAsync(request);
    }
}