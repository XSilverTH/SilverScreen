using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

/// <summary>Sends YouTube's normal playback and incremental watchtime beacons while media is playing.</summary>
public sealed class YouTubePlaybackTelemetryService : IYouTubePlaybackTelemetryService
{
    private static readonly ILogger Logger = Log.ForContext<YouTubePlaybackTelemetryService>();
    private readonly Lock _sessionsLock = new();
    private readonly IPreferencesService _preferences;
    private readonly ISessionService _sessionService;
    private readonly Func<CookieContainer, HttpMessageHandler>? _handlerFactory;
    private readonly HashSet<TelemetrySession> _sessions = [];
    private bool _disposed;

    public YouTubePlaybackTelemetryService(IPreferencesService preferences, ISessionService sessionService,
        Func<CookieContainer, HttpMessageHandler>? handlerFactory = null)
    {
        _preferences = preferences;
        _sessionService = sessionService;
        _handlerFactory = handlerFactory;
    }

    public IYouTubePlaybackTelemetrySession Start(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new TelemetrySession(this, request);
        lock (_sessionsLock)
        {
            if (_disposed)
            {
                session.Dispose();
                return NoopTelemetrySession.Instance;
            }

            _sessions.Add(session);
        }

        return session;
    }

    public void Dispose()
    {
        TelemetrySession[] sessions;
        lock (_sessionsLock)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = _sessions.ToArray();
            _sessions.Clear();
        }

        foreach (var session in sessions) session.Dispose();
    }

    private bool IsEnabled()
    {
        var preferences = _preferences.GetPreferences();
        return preferences.YouTubePlaybackTelemetryEnabled && !preferences.MarkWatchedVideos;
    }

    private void Remove(TelemetrySession session)
    {
        lock (_sessionsLock) _sessions.Remove(session);
    }

    private HttpClient? CreateAuthenticatedClient()
    {
        var manualSession = _sessionService.GetManualSessionCookies();
        if (manualSession is null) return null;

        try
        {
            var cookies = ParseCookies(manualSession);
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

    private static CookieContainer ParseCookies(ManualSessionCookies manualSession)
    {
        if (manualSession.Format is not SessionCookieFormat.NetscapeCookiesText)
            throw new NotSupportedException($"Unsupported cookie format: {manualSession.Format}");

        var cookies = new CookieContainer();
        foreach (var sourceLine in manualSession.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = sourceLine.TrimEnd('\r');
            var httpOnly = line.StartsWith("#HttpOnly_", StringComparison.Ordinal);
            if (line.StartsWith('#') && !httpOnly) continue;
            if (httpOnly) line = line[10..];

            var fields = line.Split('\t');
            if (fields.Length != 7) continue;
            var domain = fields[0];
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(fields[5])) continue;

            try
            {
                var cookie = new Cookie(fields[5], fields[6], fields[2], domain)
                {
                    Secure = bool.TryParse(fields[3], out var secure) && secure,
                    HttpOnly = httpOnly
                };
                if (long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiration) &&
                    expiration > 0)
                    cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expiration).UtcDateTime;
                cookies.Add(cookie);
            }
            catch (CookieException)
            {
                // Browser exports can contain cookies outside CookieContainer's accepted domain syntax.
            }
        }

        return cookies;
    }

    private sealed class TelemetrySession : IYouTubePlaybackTelemetrySession
    {
        private readonly YouTubePlaybackTelemetryService _owner;
        private readonly PlaybackRequest _request;
        private readonly Lock _lock = new();
        private readonly Dictionary<int, VideoTelemetrySession> _videos = [];
        private bool _disposed;

        public TelemetrySession(YouTubePlaybackTelemetryService owner, PlaybackRequest request)
        {
            _owner = owner;
            _request = request;
        }

        public void UpdateState(PlaybackPresenceState state)
        {
            if (!_owner.IsEnabled() || state.PlaylistIndex < 0 || state.PlaylistIndex >= _request.Videos.Length) return;

            lock (_lock)
            {
                if (_disposed) return;
                if (!_videos.TryGetValue(state.PlaylistIndex, out var video))
                {
                    video = new VideoTelemetrySession(_owner, _request.Videos[state.PlaylistIndex].Id);
                    _videos.Add(state.PlaylistIndex, video);
                }

                video.UpdateState(state);
            }
        }

        public void Dispose()
        {
            VideoTelemetrySession[] videos;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                videos = _videos.Values.ToArray();
                _videos.Clear();
            }

            foreach (var video in videos) video.Dispose();
            _owner.Remove(this);
        }
    }

    private sealed class VideoTelemetrySession
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
        private readonly YouTubePlaybackTelemetryService _owner;
        private readonly string _videoId;
        private readonly string _cpn = CreateCpn();
        private HttpClient? _client;
        private Task<TrackingEndpoints?>? _endpointsTask;
        private Task _sendTail = Task.CompletedTask;
        private TimeSpan _segmentStart;
        private TimeSpan _lastPosition;
        private bool _playing;
        private bool _disposed;

        public VideoTelemetrySession(YouTubePlaybackTelemetryService owner, string videoId)
        {
            _owner = owner;
            _videoId = videoId;
        }

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
            _disposed = true;
            var client = _client;
            _client = null;
            _ = DisposeClientAfterSendsAsync(_sendTail, client);
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
            try
            {
                await previous.ConfigureAwait(false);
                if (_disposed || !_owner.IsEnabled()) return;
                var endpoints = await GetEndpointsAsync().ConfigureAwait(false);
                if (endpoints is null || _client is null) return;
                var uri = telemetryEvent.BuildUri(endpoints, _cpn);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { Referrer = new Uri($"https://www.youtube.com/watch?v={_videoId}") }
                };
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    Logger.Debug("YouTube playback telemetry returned {StatusCode}", response.StatusCode);
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "YouTube playback telemetry request failed");
            }
        }

        private async Task<TrackingEndpoints?> GetEndpointsAsync()
        {
            _endpointsTask ??= InitializeEndpointsAsync();
            return await _endpointsTask.ConfigureAwait(false);
        }

        private async Task<TrackingEndpoints?> InitializeEndpointsAsync()
        {
            _client = _owner.CreateAuthenticatedClient();
            if (_client is null) return null;

            try
            {
                var pageUri = new Uri($"https://www.youtube.com/watch?v={Uri.EscapeDataString(_videoId)}&bpctr=9999999999&has_verified=1");
                using var response = await _client.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var page = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return TrackingEndpoints.TryParse(page);
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
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                    continue;
                }

                if (character == '"') quoted = true;
                else if (character == '{') depth++;
                else if (character == '}' && --depth == 0)
                {
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
        public static TelemetryEvent Playback(TimeSpan position) => new(false, position, position);
        public static TelemetryEvent Watchtime(TimeSpan start, TimeSpan end) => new(true, start, end);

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
            if (IsWatchtime)
            {
                parameters.Add(new KeyValuePair<string, string>("st", FormatSeconds(Start)));
                parameters.Add(new KeyValuePair<string, string>("et", FormatSeconds(End)));
            }

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
        public void UpdateState(PlaybackPresenceState state) { }
        public void Dispose() { }
    }
}
