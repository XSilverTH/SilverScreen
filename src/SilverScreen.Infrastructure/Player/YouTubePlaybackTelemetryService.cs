using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Player;

/// <summary>Sends YouTube's normal playback and incremental watchtime beacons while media is playing.</summary>
public sealed class YouTubePlaybackTelemetryService : IYouTubePlaybackTelemetryService
{
    // Bounded live-session set: Start-without-Dispose callers must not grow memory without limit,
    // so the oldest-tracked session is evicted (disposed) past the cap.
    private const int MaxActiveSessions = 200;
    private static readonly ILogger Logger = Log.ForContext<YouTubePlaybackTelemetryService>();
    private readonly IPreferencesService _preferences;
    private readonly ISessionService _sessionService;
    private readonly Func<CookieContainer, HttpMessageHandler>? _handlerFactory;
    private readonly HashSet<TelemetrySession> _sessions = [];
    private readonly Lock _sessionsLock = new();
    private bool _disposed;

    public YouTubePlaybackTelemetryService(
        IPreferencesService preferences,
        ISessionService sessionService,
        Func<CookieContainer, HttpMessageHandler>? handlerFactory = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _handlerFactory = handlerFactory;
        _preferences.PreferencesChanged += OnPreferencesChanged;
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public IYouTubePlaybackTelemetrySession Start(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new TelemetrySession(this, request);
        TelemetrySession? evicted = null;
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                session.Dispose();
                return NoopTelemetrySession.Instance;
            }

            if (_sessions.Count >= MaxActiveSessions)
            {
                foreach (var tracked in _sessions)
                {
                    evicted = tracked;
                    break;
                }

                if (evicted is not null)
                    _sessions.Remove(evicted);
            }

            _sessions.Add(session);
        }

        evicted?.Dispose();
        return session;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        TelemetrySession[] sessions;
        lock (_sessionsLock)
        {
            if (_disposed) return;
            sessions = [.. _sessions];
            _sessions.Clear();
        }

        foreach (var tracked in sessions) tracked.Retire();
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        var enabled = preferences is { YouTubePlaybackTelemetryEnabled: true, MarkWatchedVideos: false };
        TelemetrySession[] sessions;
        lock (_sessionsLock)
        {
            if (_disposed || enabled) return;
            sessions = [.. _sessions];
        }

        foreach (var session in sessions) session.DisableConsent();
    }

    public void Dispose()
    {
        TelemetrySession[] sessions;
        lock (_sessionsLock)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = [.. _sessions];
            _sessions.Clear();
        }

        _preferences.PreferencesChanged -= OnPreferencesChanged;
        _sessionService.SessionChanged -= OnSessionChanged;
        foreach (var session in sessions) session.Dispose();
    }

    private bool IsEnabled()
    {
        var currentPreferences = _preferences.GetPreferences();
        return currentPreferences is { YouTubePlaybackTelemetryEnabled: true, MarkWatchedVideos: false };
    }

    private void Remove(TelemetrySession session)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }
    }

    private HttpClient? CreateAuthenticatedClient()
    {
        var cookies = _sessionService.CreateCookieContainer();
        if (cookies is null) return null;

        try
        {
            var handler = _handlerFactory?.Invoke(cookies) ?? new HttpClientHandler
            {
                CookieContainer = cookies,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };
            var client = new HttpClient(handler, true);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-YouTube-Client-Name", "1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-YouTube-Client-Version", "2.20260724.01.00");
            return client;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Could not prepare authenticated YouTube playback telemetry");
            return null;
        }
    }

    private sealed class TelemetrySession : IYouTubePlaybackTelemetrySession
    {
        // Video identity, rather than playlist index, is the stable key across queue
        // reorders and removals. The active queue snapshot is replaced atomically
        // with the current entry so a later state update cannot address another video.
        private const int MaxVideosPerSession = 200;
        private readonly YouTubePlaybackTelemetryService _owner;
        private readonly Lock _lock = new();
        private readonly Dictionary<string, VideoTelemetrySession> _videos = new(StringComparer.Ordinal);
        private PlaybackRequest _request;
        private string? _currentVideoId;
        private bool _disposed;

        public TelemetrySession(YouTubePlaybackTelemetryService owner, PlaybackRequest request)
        {
            _owner = owner;
            _request = request;
        }

        public void UpdateQueue(PlaybackRequest request, int currentIndex)
        {
            ArgumentNullException.ThrowIfNull(request);
            VideoTelemetrySession? previous = null;
            lock (_lock)
            {
                if (_disposed || currentIndex < 0 || currentIndex >= request.Videos.Length) return;

                _request = request;
                var videoId = request.Videos[currentIndex].Id;
                if (!string.Equals(_currentVideoId, videoId, StringComparison.Ordinal))
                {
                    if (_currentVideoId is not null)
                        _videos.Remove(_currentVideoId, out previous);
                    _currentVideoId = videoId;
                }

            }

            previous?.Dispose();
        }

        public void UpdateState(PlaybackPresenceState state)
        {
            if (!_owner.IsEnabled()) return;

            VideoTelemetrySession? previous = null;
            VideoTelemetrySession? video;
            lock (_lock)
            {
                if (!_owner.IsEnabled() || _disposed || state.PlaylistIndex < 0 ||
                    state.PlaylistIndex >= _request.Videos.Length) return;

                var videoId = _request.Videos[state.PlaylistIndex].Id;
                if (!string.Equals(_currentVideoId, videoId, StringComparison.Ordinal))
                {
                    if (_currentVideoId is not null)
                        _videos.Remove(_currentVideoId, out previous);
                    _currentVideoId = videoId;
                }

                if (!_videos.TryGetValue(videoId, out video))
                {
                    if (_videos.Count >= MaxVideosPerSession)
                    {
                        using var entries = _videos.GetEnumerator();
                        entries.MoveNext();
                        previous ??= entries.Current.Value;
                        _videos.Remove(entries.Current.Key);
                    }

                    video = new VideoTelemetrySession(_owner, videoId);
                    _videos.Add(videoId, video);
                }

                video.UpdateState(state);
            }

            previous?.Dispose();
        }
        public void DisableConsent()
        {
            VideoTelemetrySession[] videos;
            lock (_lock)
            {
                if (_disposed) return;
                videos = [.. _videos.Values];
                _videos.Clear();
                _currentVideoId = null;
            }

            foreach (var video in videos) video.InvalidateConsent();
        }

        public void Dispose()
        {
            VideoTelemetrySession[] videos;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                videos = [.. _videos.Values];
                _videos.Clear();
            }

            foreach (var video in videos) video.Dispose();
            _owner.Remove(this);
        }

        public void Retire()
        {
            VideoTelemetrySession[] videos;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                videos = [.. _videos.Values];
                _videos.Clear();
            }

            foreach (var video in videos) video.Retire();
            _owner.Remove(this);
        }
    }

    private sealed class VideoTelemetrySession(YouTubePlaybackTelemetryService owner, string videoId)
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
        private readonly string _cpn = CreateCpn();
        private readonly Lock _consentLock = new();
        private readonly CancellationTokenSource _consentCancellation = new();
        private HttpClient? _client;
        private bool _disposed;
        private Task<TrackingEndpoints?>? _endpointsTask;
        private TimeSpan _lastPosition;
        private bool _playing;
        private TimeSpan _segmentStart;
        private Task _sendTail = Task.CompletedTask;

        public void UpdateState(PlaybackPresenceState state)
        {
            if (_disposed) return;
            var position = state.Position < TimeSpan.Zero ? TimeSpan.Zero : state.Position;
            if (state.IsPaused)
            {
                FlushSegment(position);
                _playing = false;
                _lastPosition = position;
                return;
            }

            if (!_playing)
            {
                _playing = true;
                _segmentStart = position;
                _lastPosition = position;
                Enqueue(TelemetryEvent.Playback(position));
                return;
            }

            if (position < _lastPosition)
            {
                FlushSegment(_lastPosition);
                _segmentStart = position;
            }
            else if (position - _segmentStart >= HeartbeatInterval)
            {
                Enqueue(TelemetryEvent.Watchtime(_segmentStart, position));
                _segmentStart = position;
            }

            _lastPosition = position;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_playing)
            {
                FlushSegment(_lastPosition);
                _playing = false;
            }

            _disposed = true;
            var client = _client;
            _client = null;
            DisposeClientAfterSendsAsync(_sendTail, client).FireAndForget(Logger);
        }
        public void InvalidateConsent()
        {
            lock (_consentLock)
            {
                if (_disposed) return;
                _disposed = true;
                _playing = false;
                _lastPosition = TimeSpan.Zero;
                _segmentStart = TimeSpan.Zero;
                _consentCancellation.Cancel();
                _client?.Dispose();
                _client = null;
            }
        }

        public void Retire()
        {
            if (_disposed) return;
            _disposed = true;
            _consentCancellation.Cancel();
            _client?.Dispose();
            _client = null;
        }
        private void FlushSegment(TimeSpan end)
        {
            if (!_playing || end <= _segmentStart) return;
            Enqueue(TelemetryEvent.Watchtime(_segmentStart, end));
        }

        private void Enqueue(TelemetryEvent telemetryEvent)
        {
            _sendTail = SendAfterAsync(_sendTail, telemetryEvent);
        }
        private async Task SendAfterAsync(Task previous, TelemetryEvent telemetryEvent)
        {
            var cancellationToken = _consentCancellation.Token;
            try
            {
                await previous.ConfigureAwait(false);
                if (!CanSend(cancellationToken)) return;
                var endpoints = await GetEndpointsAsync(cancellationToken).ConfigureAwait(false);
                if (!CanSend(cancellationToken) || endpoints is null) return;

                var uri = telemetryEvent.BuildUri(endpoints, _cpn);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Referrer = new Uri($"https://www.youtube.com/watch?v={videoId}");
                Task<HttpResponseMessage> sendTask;
                lock (_consentLock)
                {
                    if (!CanSend(cancellationToken) || _client is null) return;
                    sendTask = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }

                using var response = await sendTask.ConfigureAwait(false);
                if (!CanSend(cancellationToken)) return;
                if (!response.IsSuccessStatusCode)
                    Logger.Debug("YouTube playback telemetry returned {StatusCode}", response.StatusCode);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "YouTube playback telemetry request failed");
            }
        }

        private bool CanSend(CancellationToken cancellationToken)
        {
            return !cancellationToken.IsCancellationRequested && !_disposed && owner.IsEnabled();
        }

        private async Task<TrackingEndpoints?> GetEndpointsAsync(CancellationToken cancellationToken)
        {
            _endpointsTask ??= InitializeEndpointsAsync(cancellationToken);
            return await _endpointsTask.ConfigureAwait(false);
        }

        private async Task<TrackingEndpoints?> InitializeEndpointsAsync(CancellationToken cancellationToken)
        {
            var client = owner.CreateAuthenticatedClient();
            if (client is null) return null;

            lock (_consentLock)
            {
                if (!CanSend(cancellationToken))
                {
                    client.Dispose();
                    return null;
                }

                _client = client;
            }

            try
            {
                var pageUri =
                    new Uri(
                        $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}&bpctr=9999999999&has_verified=1");
                using var response = await client.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!CanSend(cancellationToken) || !response.IsSuccessStatusCode) return null;
                var page = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return CanSend(cancellationToken) ? TrackingEndpoints.TryParse(page) : null;
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Could not load YouTube playback tracking endpoints");
                return null;
            }
        }

        private static async Task DisposeClientAfterSendsAsync(Task sendTail, HttpClient? client)
        {
            try
            {
                await sendTail.ConfigureAwait(false);
            }
            catch
            {
                // Individual telemetry sends already handle their own errors.
            }
            finally
            {
                client?.Dispose();
            }
        }

        private static string CreateCpn()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);
            return string.Create(16, bytes.ToArray(), static (result, source) =>
            {
                for (var index = 0; index < source.Length; index++) result[index] = alphabet[source[index] & 63];
            });
        }
    }

    private sealed record TrackingEndpoints(Uri Playback, Uri Watchtime)
    {
        public static TrackingEndpoints? TryParse(string page)
        {
            var playerResponse = ExtractPlayerResponse(page);
            if (playerResponse is null) return null;

            using (playerResponse)
            {
                if (!playerResponse.RootElement.TryGetProperty("playbackTracking", out var tracking) ||
                    !TryGetUrl(tracking, "videostatsPlaybackUrl", out var playback) ||
                    !TryGetUrl(tracking, "videostatsWatchtimeUrl", out var watchtime))
                    return null;
                return new TrackingEndpoints(playback, watchtime);
            }
        }

        private static JsonDocument? ExtractPlayerResponse(string page)
        {
            const string marker = "ytInitialPlayerResponse";
            var markerIndex = page.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0) return null;
            var objectStart = page.IndexOf('{', markerIndex + marker.Length);
            if (objectStart < 0) return null;

            var depth = 0;
            var escaped = false;
            var quoted = false;
            for (var index = objectStart; index < page.Length; index++)
            {
                var character = page[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else
                        switch (character)
                        {
                            case '\\':
                                escaped = true;
                                break;
                            case '"':
                                quoted = false;
                                break;
                        }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        quoted = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}' when --depth == 0:
                        try
                        {
                            return JsonDocument.Parse(page.AsMemory(objectStart, index - objectStart + 1));
                        }
                        catch (JsonException)
                        {
                            return null;
                        }
                }
            }

            return null;
        }

        private static bool TryGetUrl(JsonElement tracking, string property, out Uri url)
        {
            if (tracking.TryGetProperty(property, out var endpoint) &&
                endpoint.TryGetProperty("baseUrl", out var baseUrl) &&
                Uri.TryCreate(baseUrl.GetString(), UriKind.Absolute, out var parsedUrl) &&
                string.Equals(parsedUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                url = parsedUrl;
                return true;
            }

            url = null!;
            return false;
        }
    }

    private readonly record struct TelemetryEvent(bool IsWatchtime, TimeSpan Start, TimeSpan End)
    {
        public static TelemetryEvent Playback(TimeSpan position)
        {
            return new TelemetryEvent(false, position, position);
        }

        public static TelemetryEvent Watchtime(TimeSpan start, TimeSpan end)
        {
            return new TelemetryEvent(true, start, end);
        }

        public Uri BuildUri(TrackingEndpoints endpoints, string cpn)
        {
            var endpoint = IsWatchtime ? endpoints.Watchtime : endpoints.Playback;
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("ver", "2"),
                new("cpn", cpn),
                new("cmt", FormatSeconds(End)),
                new("el", "detailpage")
            };
            if (!IsWatchtime) return AppendParameters(endpoint, parameters);
            parameters.Add(new KeyValuePair<string, string>("st", FormatSeconds(Start)));
            parameters.Add(new KeyValuePair<string, string>("et", FormatSeconds(End)));

            return AppendParameters(endpoint, parameters);
        }

        private static Uri AppendParameters(Uri endpoint, IReadOnlyList<KeyValuePair<string, string>> parameters)
        {
            var reserved = parameters.Select(parameter => parameter.Key).ToHashSet(StringComparer.Ordinal);
            var existing = endpoint.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(parameter => !reserved.Contains(Uri.UnescapeDataString(parameter.Split('=', 2)[0])));
            var additions = parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}");
            var query = string.Join('&', existing.Concat(additions));
            var builder = new UriBuilder(endpoint) { Query = query };
            return builder.Uri;
        }

        private static string FormatSeconds(TimeSpan value)
        {
            return Math.Max(0, value.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NoopTelemetrySession : IYouTubePlaybackTelemetrySession
    {
        public static NoopTelemetrySession Instance { get; } = new();

        public void UpdateQueue(PlaybackRequest request, int currentIndex)
        {
        }

        public void UpdateState(PlaybackPresenceState state)
        {
        }

        public void Dispose()
        {
        }
    }
}