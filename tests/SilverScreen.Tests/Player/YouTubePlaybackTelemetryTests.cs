using System.Net;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Tests.Player;

public sealed class YouTubePlaybackTelemetryTests
{
    [Fact]
    public async Task PlayingAndPausingSendsIncrementalYouTubeWatchtimeBeacons()
    {
        var handler = new TrackingHandler();
        using var service = new YouTubePlaybackTelemetryService(new MutablePreferencesService(true),
            new ManualSessionService(), _ => handler);
        using var telemetry = service.Start(CreateRequest());

        telemetry.UpdateState(State(0, false));
        telemetry.UpdateState(State(12, false));
        telemetry.UpdateState(State(14, true));

        var beacons = await handler.WaitForBeaconsAsync();

        Assert.Collection(beacons,
            playback =>
            {
                Assert.Equal("/api/stats/playback", playback.AbsolutePath);
                Assert.Equal("0", QueryValue(playback, "cmt"));
                Assert.Equal("detailpage", QueryValue(playback, "el"));
            },
            firstWatchtime =>
            {
                Assert.Equal("/api/stats/watchtime", firstWatchtime.AbsolutePath);
                Assert.Equal("0", QueryValue(firstWatchtime, "st"));
                Assert.Equal("12", QueryValue(firstWatchtime, "et"));
            },
            pausedWatchtime =>
            {
                Assert.Equal("/api/stats/watchtime", pausedWatchtime.AbsolutePath);
                Assert.Equal("12", QueryValue(pausedWatchtime, "st"));
                Assert.Equal("14", QueryValue(pausedWatchtime, "et"));
            });
        Assert.Single(beacons.Select(uri => QueryValue(uri, "cpn")).Distinct());
        Assert.All(beacons, beacon => Assert.Equal("2", QueryValue(beacon, "ver")));
    }

    [Fact]
    public async Task SignOutRetiresInProgressTelemetryAndRefusesFutureBeacons()
    {
        var handler = new TrackingHandler();
        var session = new ManualSessionService();
        using var service = new YouTubePlaybackTelemetryService(new MutablePreferencesService(true), session, _ => handler);
        using var telemetry = service.Start(CreateRequest());

        telemetry.UpdateState(State(0, false));
        await handler.WaitForBeaconsAsync(1);

        session.ClearSession();
        telemetry.UpdateState(State(12, true));

        await Task.Delay(100);
        Assert.Equal(1, handler.BeaconCount);
    }

    [Fact]
    public async Task QueueReorderKeepsWatchtimeBeaconOnTheCurrentVideo()
    {
        var handler = new TrackingHandler();
        using var service = new YouTubePlaybackTelemetryService(new MutablePreferencesService(true),
            new ManualSessionService(), _ => handler);
        var first = new VideoSummary("abc123_X-yZ", "First", "Channel", TimeSpan.FromMinutes(1),
            "https://i.ytimg.com/vi/abc123_X-yZ/default.jpg", false);
        var second = new VideoSummary("dQw4w9WgXcQ", "Second", "Channel", TimeSpan.FromMinutes(1),
            "https://i.ytimg.com/vi/dQw4w9WgXcQ/default.jpg", false);
        using var telemetry = service.Start(new PlaybackRequest([first, second]));

        telemetry.UpdateState(new PlaybackPresenceState(1, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, 1,
            DateTimeOffset.UtcNow));
        telemetry.UpdateQueue(new PlaybackRequest([second, first]), 0);
        telemetry.UpdateState(new PlaybackPresenceState(0, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, 1,
            DateTimeOffset.UtcNow));
        telemetry.UpdateState(new PlaybackPresenceState(0, TimeSpan.FromSeconds(12), TimeSpan.FromMinutes(1), true, 1,
            DateTimeOffset.UtcNow));

        await handler.WaitForBeaconsAsync(2);
        Assert.All(handler.Referrers, referrer => Assert.EndsWith("dQw4w9WgXcQ", referrer));
    }


    [Fact]
    public async Task OptingOutWhileBeaconIsPendingCancelsTheBeacon()
    {
        var handler = new BlockingBeaconHandler();
        var preferences = new MutablePreferencesService(true);
        using var service = new YouTubePlaybackTelemetryService(preferences, new ManualSessionService(), _ => handler);
        using var telemetry = service.Start(CreateRequest());

        telemetry.UpdateState(State(0, false));
        await handler.BeaconStarted.WaitAsync(TimeSpan.FromSeconds(2));

        preferences.SetEnabled(false);
        handler.ReleaseBeacon();
        await Task.Delay(100);

        Assert.Equal(0, handler.BeaconCount);
    }

    [Fact]
    public async Task TogglingConsentOffAndOnStartsFreshWatchtimeSegments()
    {
        var handler = new TrackingHandler();
        var preferences = new MutablePreferencesService(true);
        using var service = new YouTubePlaybackTelemetryService(preferences, new ManualSessionService(), _ => handler);
        using var telemetry = service.Start(CreateRequest());

        telemetry.UpdateState(State(0, false));
        telemetry.UpdateState(State(12, false));
        await handler.WaitUntilCountAsync(2);

        preferences.SetEnabled(false);
        telemetry.UpdateState(State(60, false));
        preferences.SetEnabled(true);
        telemetry.UpdateState(State(20, false));
        telemetry.UpdateState(State(32, true));
        await handler.WaitUntilCountAsync(4);

        var beacons = handler.Snapshot;
        Assert.Equal(4, beacons.Count);
        Assert.Equal(1, beacons.Count(uri => uri.AbsolutePath == "/api/stats/watchtime" &&
            QueryValue(uri, "st") == "0" && QueryValue(uri, "et") == "12"));
        Assert.Contains(beacons, uri => uri.AbsolutePath == "/api/stats/playback" && QueryValue(uri, "cmt") == "20");
        Assert.Contains(beacons, uri => uri.AbsolutePath == "/api/stats/watchtime" &&
            QueryValue(uri, "st") == "20" && QueryValue(uri, "et") == "32");
    }

    private static PlaybackRequest CreateRequest()
    {
        return new PlaybackRequest([
            new VideoSummary("abc123_X-yZ", "Video", "Channel", TimeSpan.FromMinutes(1),
                "https://i.ytimg.com/vi/abc123_X-yZ/default.jpg", false)
        ]);
    }

    private static PlaybackPresenceState State(double position, bool paused)
    {
        return new PlaybackPresenceState(0, TimeSpan.FromSeconds(position), TimeSpan.FromMinutes(1), paused, 1,
            DateTimeOffset.UtcNow);
    }

    private static string? QueryValue(Uri uri, string name)
    {
        return uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => string.Equals(Uri.UnescapeDataString(pair[0]), name, StringComparison.Ordinal))
            .Select(pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty)
            .SingleOrDefault();
    }

    private sealed class MutablePreferencesService(bool enabled, bool markWatched = false) : IPreferencesService
    {
        private AppPreferences _preferences = new()
        {
            YouTubePlaybackTelemetryEnabled = enabled,
            MarkWatchedVideos = markWatched
        };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }
        public void SetEnabled(bool enabled)
        {
            SavePreferences(_preferences with { YouTubePlaybackTelemetryEnabled = enabled });
        }

        public void SavePreferences(AppPreferences preferences)
        {
            _preferences = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class ManualSessionService : ISessionService
    {
        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession()
        {
            return new AccountSession(true, HasManualSession: true);
        }

        public ManualSessionCookies GetManualSessionCookies()
        {
            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText,
                ".youtube.com\tTRUE\t/\tTRUE\t0\tSID\tvalue\n");
        }

        public CookieFileLease? AcquireCookieFileLease()
        {
            return null;
        }

        public CookieFileLease? CreateCookieFile()
        {
            return null;
        }
        public CookieContainer? CreateCookieContainer()
        {
            return NetscapeCookieParser.CreateCookieContainer(".youtube.com\tTRUE\t/\tTRUE\t0\tSID\tvalue\n");
        }


        public void SetManualSession(string cookieContent, SessionCookieFormat format)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSession()
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private const string PlayerResponse = """
                                              <script>var ytInitialPlayerResponse = {"playbackTracking":{"videostatsPlaybackUrl":{"baseUrl":"https://s.youtube.com/api/stats/playback?docid=abc123_X-yZ&len=60&ns=yt"},"videostatsWatchtimeUrl":{"baseUrl":"https://s.youtube.com/api/stats/watchtime?docid=abc123_X-yZ&len=60&ns=yt"}}};</script>
                                              """;

        private readonly List<Uri> _beacons = [];
        private readonly List<string> _referrers = [];
        private readonly TaskCompletionSource<IReadOnlyList<Uri>> _beaconsReceived = new();
        private readonly Lock _lock = new();
        private int _expectedBeaconCount = 3;

        public IReadOnlyList<string> Referrers
        {
            get
            {
                lock (_lock) return [.. _referrers];
            }
        }
        public IReadOnlyList<Uri> Snapshot
        {
            get
            {
                lock (_lock) return [.. _beacons];
            }
        }

        public async Task WaitUntilCountAsync(int expectedCount)
        {
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (BeaconCount < expectedCount && DateTime.UtcNow < timeout)
                await Task.Delay(10);
            Assert.True(BeaconCount >= expectedCount, $"Expected at least {expectedCount} beacons.");
        }

        public int BeaconCount
        {
            get
            {
                lock (_lock) return _beacons.Count;
            }
        }

        public Task<IReadOnlyList<Uri>> WaitForBeaconsAsync(int expectedCount = 3)
        {
            lock (_lock)
            {
                _expectedBeaconCount = expectedCount;
                if (_beacons.Count >= expectedCount) _beaconsReceived.TrySetResult([.. _beacons]);
            }

            return _beaconsReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (_lock)
            {
                if (uri is { Host: "www.youtube.com", AbsolutePath: "/watch" })
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(PlayerResponse)
                    });

                _beacons.Add(uri);
                if (request.Headers.Referrer is { } referrer) _referrers.Add(referrer.ToString());
                if (_beacons.Count >= _expectedBeaconCount) _beaconsReceived.TrySetResult([.. _beacons]);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
    private sealed class BlockingBeaconHandler : HttpMessageHandler
    {
        private const string PlayerResponse = """
                                              <script>var ytInitialPlayerResponse = {"playbackTracking":{"videostatsPlaybackUrl":{"baseUrl":"https://s.youtube.com/api/stats/playback"},"videostatsWatchtimeUrl":{"baseUrl":"https://s.youtube.com/api/stats/watchtime"}}};</script>
                                              """;
        private readonly TaskCompletionSource _beaconStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBeacon = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _beaconCount;

        public Task BeaconStarted => _beaconStarted.Task;
        public int BeaconCount => Volatile.Read(ref _beaconCount);

        public void ReleaseBeacon()
        {
            _releaseBeacon.TrySetResult();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is { Host: "www.youtube.com", AbsolutePath: "/watch" })
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(PlayerResponse)
                });

            _beaconStarted.TrySetResult();
            return WaitForBeaconReleaseAsync(cancellationToken);
        }

        private async Task<HttpResponseMessage> WaitForBeaconReleaseAsync(CancellationToken cancellationToken)
        {
            await _releaseBeacon.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _beaconCount);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}