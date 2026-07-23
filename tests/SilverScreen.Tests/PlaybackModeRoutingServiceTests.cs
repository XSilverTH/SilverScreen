using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Views.Player;

namespace SilverScreen.Tests;

public sealed class PlaybackModeRoutingServiceTests
{
    [Fact]
    public async Task PlayAsync_UsesTheCurrentPlaybackMode()
    {
        var preferences = new MutablePreferencesService(PlaybackBackends.ExternalMpv);
        var externalMpv = new RecordingPlaybackService("Opening in MPV.");
        var embeddedPlayer = new RecordingEmbeddedPlayer();
        var router = new PlaybackModeRoutingService(preferences, externalMpv, embeddedPlayer);
        var request = new PlaybackRequest([
            new VideoSummary("abc123def45", "Test video", "Test channel", TimeSpan.FromMinutes(2), "", false)
        ]);

        Assert.Equal("Opening in MPV.", await router.PlayAsync(request));
        Assert.Single(externalMpv.Requests);
        Assert.Empty(embeddedPlayer.Requests);

        preferences.Current.PlaybackBackend = PlaybackBackends.EmbeddedPlayer;

        Assert.Equal("Opening embedded player.", await router.PlayAsync(request));
        Assert.Single(externalMpv.Requests);
        Assert.Single(embeddedPlayer.Requests);
    }

    private sealed class MutablePreferencesService(string playbackBackend) : IPreferencesService
    {
        public AppPreferences Current { get; } = new() { PlaybackBackend = playbackBackend };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences() => Current;

        public void SavePreferences(AppPreferences preferences)
        {
            Current.PlaybackBackend = preferences.PlaybackBackend;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class RecordingPlaybackService(string status) : IPlaybackService
    {
        public List<PlaybackRequest> Requests { get; } = [];

        public Task<string> PlayAsync(PlaybackRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(status);
        }
    }

    private sealed class RecordingEmbeddedPlayer : IEmbeddedPlayerPresenter
    {
        public List<PlaybackRequest> Requests { get; } = [];

        public Task<string> PresentAsync(PlaybackRequest request)
        {
            Requests.Add(request);
            return Task.FromResult("Opening embedded player.");
        }
    }
}
