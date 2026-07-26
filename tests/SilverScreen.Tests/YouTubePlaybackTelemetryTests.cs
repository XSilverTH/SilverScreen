using System.Net;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

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

        public AppPreferences GetPreferences() => _preferences;

        public void SavePreferences(AppPreferences preferences)
        {
            _preferences = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class ManualSessionService : ISessionService
    {
        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession() => new(true, HasManualSession: true);

        public ManualSessionCookies? GetManualSessionCookies()
        {
            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText,
                ".youtube.com\tTRUE\t/\tTRUE\t0\tSID\tvalue\n");
        }

        public void SetManualSession(string cookieContent, SessionCookieFormat format) => SessionChanged?.Invoke(this, EventArgs.Empty);
        public void ClearSession() => SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private const string PlayerResponse = """
            <script>var ytInitialPlayerResponse = {"playbackTracking":{"videostatsPlaybackUrl":{"baseUrl":"https://s.youtube.com/api/stats/playback?docid=abc123_X-yZ&len=60&ns=yt"},"videostatsWatchtimeUrl":{"baseUrl":"https://s.youtube.com/api/stats/watchtime?docid=abc123_X-yZ&len=60&ns=yt"}}};</script>
            """;
        private readonly TaskCompletionSource<IReadOnlyList<Uri>> _beaconsReceived = new();
        private readonly Lock _lock = new();
        private readonly List<Uri> _requests = [];
        private readonly List<Uri> _beacons = [];

        public IReadOnlyList<Uri> Requests
        {
            get
            {
                lock (_lock) return _requests.ToArray();
            }
        }

        public IReadOnlyList<Uri> Beacons
        {
            get
            {
                lock (_lock) return _beacons.ToArray();
            }
        }

        public Task<IReadOnlyList<Uri>> WaitForBeaconsAsync()
        {
            return _beaconsReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (_lock)
            {
                _requests.Add(uri);
                if (uri.Host == "www.youtube.com" && uri.AbsolutePath == "/watch")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(PlayerResponse)
                    });

                _beacons.Add(uri);
                if (_beacons.Count == 3) _beaconsReceived.TrySetResult(_beacons.ToArray());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
