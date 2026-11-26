using System.Collections.Immutable;
using Serilog;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Player;

internal sealed class PlaybackModeRoutingService(
    IPreferencesService preferencesService,
    IPlaybackService externalMpvPlayback,
    IEmbeddedPlayerPresenter embeddedPlayer,
    ILibraryService? libraryService = null,
    INotificationService? notifications = null)
    : IPlaybackModeRoutingService
{
    private static readonly ILogger Logger = Log.ForContext<PlaybackModeRoutingService>();
    public bool HasMedia => embeddedPlayer.HasMedia;
    public bool IsPaused => embeddedPlayer.IsPaused;

    public Task<string> PlayAsync(PlaybackRequest request)
    {
        var resolved = ResolveLibraryCopies(request);
        var backend = preferencesService.GetPreferences().PlaybackBackendKind;
        Logger.Debug("Starting playback for video using {Backend}", backend);
        return PlaybackBackends.IsEmbedded(backend)
            ? embeddedPlayer.PresentAsync(resolved)
            : externalMpvPlayback.PlayAsync(resolved);
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
        var resolved = ResolveLibraryCopies(request);
        var backend = preferencesService.GetPreferences().PlaybackBackendKind;
        Logger.Debug("Starting playback for video with alternate {Backend}", backend);
        return PlaybackBackends.IsEmbedded(backend)
            ? externalMpvPlayback.PlayAsync(resolved)
            : embeddedPlayer.PresentAsync(resolved);
    }

    private PlaybackRequest ResolveLibraryCopies(PlaybackRequest request)
    {
        if (libraryService is null || request.Videos.IsDefaultOrEmpty)
            return request;

        var prefs = preferencesService.GetPreferences();
        var updated = new VideoSummary[request.Videos.Length];
        LibraryEntry? startEntry = null;
        var changed = false;

        for (var i = 0; i < request.Videos.Length; i++)
        {
            var video = request.Videos[i];
            if (!string.IsNullOrWhiteSpace(video.LocalFilePath))
            {
                updated[i] = video;
                if (i == request.EffectiveStartIndex)
                    startEntry = libraryService.TryGetPlayableEntry(video.Id);
                continue;
            }

            var entry = libraryService.TryGetPlayableEntry(video.Id);
            if (entry is not null)
            {
                updated[i] = entry.ToPlayableVideoSummary(libraryService.LibraryDirectory);
                changed = true;
                if (i == request.EffectiveStartIndex)
                    startEntry = entry;
            }
            else
            {
                updated[i] = video;
            }
        }

        if (startEntry is not null &&
            QualityMismatchHelper.TryGetMismatchMessage(startEntry, prefs.Quality, out var mismatchMessage))
            notifications?.ShowToast(mismatchMessage);

        return changed ? request with { Videos = ImmutableArray.Create(updated) } : request;
    }
}