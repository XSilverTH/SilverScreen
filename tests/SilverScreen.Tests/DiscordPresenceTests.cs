using DiscordRPC.Entities;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class DiscordPresenceTests
{
    [Fact]
    public void FailedInitializationDisposesAndRetriesWithCachedActivity()
    {
        var preferences = new MutablePreferencesService();
        var clients = new List<TrackingClient>();
        var first = new TrackingClient { InitializeResult = false };
        var second = new TrackingClient();
        var request = CreateRequest();
        using var service = new DiscordPresenceService(preferences, "123", _ =>
        {
            var client = clients.Count == 0 ? first : second;
            clients.Add(client);
            return client;
        });

        service.SetPlaybackState(request, PlayingState());
        preferences.SetEnabled(true);

        Assert.Equal(1, first.InitializeCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Empty(first.Presences);

        service.SetPlaybackState(request, PlayingState());

        Assert.Equal(2, clients.Count);
        Assert.Equal(1, second.InitializeCount);
        Assert.Single(second.Presences);
    }

    [Fact]
    public void DisablingDisposesAndReenablingRestoresCachedActivity()
    {
        var preferences = new MutablePreferencesService(true);
        var clients = new List<TrackingClient>();
        var request = CreateRequest();
        var startedAt = DateTimeOffset.UtcNow;
        using var service = new DiscordPresenceService(preferences, "123", _ => AddClient(clients));

        service.SetPlaybackState(request, PlayingState(startedAt));
        var first = Assert.Single(clients);
        preferences.SetEnabled(false);

        Assert.Equal(1, first.ClearCount);
        Assert.Equal(1, first.DisposeCount);

        preferences.SetEnabled(true);

        Assert.Equal(2, clients.Count);
        var second = clients[1];
        var replay = Assert.Single(second.Presences);
        Assert.NotNull(replay.Timestamps);
        Assert.Equal(startedAt.UtcDateTime, replay.Timestamps.Start);
    }

    [Fact]
    public void ClearDropsCachedActivityBeforeLaterEnable()
    {
        var preferences = new MutablePreferencesService(true);
        var clients = new List<TrackingClient>();
        using var service = new DiscordPresenceService(preferences, "123", _ => AddClient(clients));

        service.SetPlaybackState(CreateRequest(), PlayingState());
        service.Clear();
        preferences.SetEnabled(false);
        preferences.SetEnabled(true);

        Assert.Single(clients);
        Assert.Single(clients[0].Presences);
    }


    [Fact]
    public void RpcExceptionsAreFailureIsolated()
    {
        var preferences = new MutablePreferencesService(true);
        var initializeFailure = new TrackingClient { ThrowOnInitialize = true };
        var setFailure = new TrackingClient { ThrowOnSet = true, ThrowOnClear = true, ThrowOnDispose = true };
        var clients = new Queue<TrackingClient>([initializeFailure, setFailure]);
        var exception = Record.Exception(() =>
        {
            using var service = new DiscordPresenceService(preferences, "123", _ => clients.Dequeue());
            service.SetPlaybackState(CreateRequest(), PlayingState());
            service.SetPlaybackState(CreateRequest(), PlayingState());
            service.Clear();
        });

        Assert.Null(exception);
        Assert.Equal(1, initializeFailure.DisposeCount);
        Assert.Equal(2, setFailure.ClearCount);
        Assert.Equal(1, setFailure.DisposeCount);
    }


    private static TrackingClient AddClient(ICollection<TrackingClient> clients)
    {
        var client = new TrackingClient();
        clients.Add(client);
        return client;
    }

    private static PlaybackRequest CreateRequest()
    {
        return new PlaybackRequest([
            new VideoSummary("abc123_X-yZ", "Video abc123_X-yZ", "Test Channel", TimeSpan.FromMinutes(3),
                "https://i.ytimg.com/vi/abc/maxresdefault.jpg", false)
        ]);
    }

    [Fact]
    public void PausedPlaybackHasNoRunningTimestampAndResumesFromItsPosition()
    {
        var preferences = new MutablePreferencesService(true);
        var clients = new List<TrackingClient>();
        var request = CreateRequest();
        var pausedAt = DateTimeOffset.UtcNow;
        using var service = new DiscordPresenceService(preferences, "123", _ => AddClient(clients));

        service.SetPlaybackState(request, new PlaybackPresenceState(0, TimeSpan.FromSeconds(45),
            TimeSpan.FromMinutes(3), true, 1, pausedAt));
        service.SetPlaybackState(request, new PlaybackPresenceState(0, TimeSpan.FromSeconds(45),
            TimeSpan.FromMinutes(3), false, 1, pausedAt.AddSeconds(2)));

        var paused = clients[0].Presences[0];
        var resumed = clients[0].Presences[1];
        Assert.Null(paused.Timestamps);
        Assert.Equal("Paused · by Test Channel", paused.State);
        Assert.NotNull(resumed.Timestamps);
        Assert.Equal(pausedAt.AddSeconds(-43).UtcDateTime, resumed.Timestamps!.Start);
    }

    private static PlaybackPresenceState PlayingState(DateTimeOffset? observedAt = null)
    {
        return new PlaybackPresenceState(0, TimeSpan.Zero, TimeSpan.FromMinutes(3), false, 1,
            observedAt ?? DateTimeOffset.UtcNow);
    }

    private sealed class MutablePreferencesService : IPreferencesService
    {
        private AppPreferences _preferences;


        public MutablePreferencesService(bool enabled = false)
        {
            _preferences = new AppPreferences { DiscordRichPresenceEnabled = enabled };
        }

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            _preferences = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }

        public void SetEnabled(bool enabled)
        {
            SavePreferences(new AppPreferences { DiscordRichPresenceEnabled = enabled });
        }
    }

    private sealed class TrackingClient : IDiscordRpcClient
    {
        public int ClearCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int InitializeCount { get; private set; }
        public bool InitializeResult { get; init; } = true;
        public List<RichPresence> Presences { get; } = [];
        public bool ThrowOnClear { get; init; }
        public bool ThrowOnDispose { get; init; }
        public bool ThrowOnInitialize { get; init; }
        public bool ThrowOnSet { get; init; }
        public event EventHandler? Ready { add { } remove { } }
        public event EventHandler? ConnectionFailed { add { } remove { } }

        public bool Initialize()
        {
            InitializeCount++;
            if (ThrowOnInitialize) throw new InvalidOperationException("initialize");
            return InitializeResult;
        }

        public void SetPresence(RichPresence presence)
        {
            Presences.Add(presence);
            if (ThrowOnSet) throw new InvalidOperationException("set");
        }

        public void ClearPresence()
        {
            ClearCount++;
            if (ThrowOnClear) throw new InvalidOperationException("clear");
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose) throw new InvalidOperationException("dispose");
        }
    }
}