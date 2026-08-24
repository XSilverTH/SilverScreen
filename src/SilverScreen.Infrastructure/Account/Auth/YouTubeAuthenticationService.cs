using System.Net.Http.Headers;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Account.Auth;

public sealed class YouTubeAuthenticationService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YouTubeAuthenticationService>();
    private readonly SemaphoreSlim _bootstrapGate = new(1, 1);
    private readonly Lock _gate = new();
    private readonly YouTubeWebOptions _options;
    private readonly ISessionService _sessionService;
    private YouTubeAuthenticatedSession? _cachedSession;
    private bool _disposed;
    private long _sessionVersion;

    public YouTubeAuthenticationService(ISessionService sessionService, YouTubeWebOptions? options = null)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _options = options ?? new YouTubeWebOptions();
        _sessionService.SessionChanged += OnSessionChanged;
    }

    internal Func<long> TimeSource { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal int? AuthUser => _options.AuthUser;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionService.SessionChanged -= OnSessionChanged;
        _bootstrapGate.Dispose();
    }

    internal event EventHandler? CredentialsChanged;

    internal YouTubeCredentialSnapshot? GetCurrentCredentials()
    {
        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || cookies.Format != SessionCookieFormat.NetscapeCookiesText ||
            string.IsNullOrWhiteSpace(cookies.Content))
            return null;
        var credentials = YouTubeCredentials.ParseNetscape(cookies.Content);
        if (credentials is null) return null;
        lock (_gate)
        {
            return new YouTubeCredentialSnapshot(credentials, _sessionVersion);
        }
    }

    internal bool IsCurrent(long sessionVersion)
    {
        lock (_gate)
        {
            return !_disposed && sessionVersion == _sessionVersion;
        }
    }

    internal async Task<YouTubeAuthenticatedSession?> GetCurrentAsync(HttpClient bootstrapClient,
        bool includeRatingBootstrapHeaders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bootstrapClient);
        var snapshot = GetCurrentCredentials();
        if (snapshot is null)
        {
            Logger.Debug("No current credentials available for YouTube authenticated session");
            return null;
        }

        await _bootstrapGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(snapshot.SessionVersion)) return null;
            lock (_gate)
            {
                if (_cachedSession is { } cached && cached.CredentialSnapshot.SessionVersion == snapshot.SessionVersion)
                    return cached;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, YouTubeWebOptions.Referer);
            request.Headers.UserAgent.ParseAdd(YouTubeWebOptions.UserAgent);
            request.Headers.Add("Origin", YouTubeWebOptions.Origin);
            request.Headers.Add("Cookie", snapshot.Credentials.CookieHeader);
            if (includeRatingBootstrapHeaders)
                request.Headers.Add("Referer", YouTubeWebOptions.Referer);
            using var response = await bootstrapClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("Failed to bootstrap YouTube session config: HTTP status {StatusCode}",
                    response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var config = YouTubeConfigBootstrap.Extract(html);
            if (config is null || !IsCurrent(snapshot.SessionVersion))
            {
                Logger.Warning("Failed to extract YouTube bootstrap config from HTML response");
                return null;
            }

            Logger.Information("Successfully bootstrapped YouTube session config (ClientVersion: {ClientVersion})",
                config.ClientVersion);
            var authenticated = new YouTubeAuthenticatedSession(snapshot, config);
            lock (_gate)
            {
                if (_disposed || snapshot.SessionVersion != _sessionVersion) return null;
                _cachedSession = authenticated;
            }

            return authenticated;
        }
        finally
        {
            _bootstrapGate.Release();
        }
    }

    internal void ApplyWatchPageHeaders(HttpRequestMessage request, YouTubeCredentialSnapshot snapshot,
        bool includeAuthUser)
    {
        request.Headers.UserAgent.ParseAdd(YouTubeWebOptions.UserAgent);
        request.Headers.Add("Origin", YouTubeWebOptions.Origin);
        request.Headers.Add("Referer", YouTubeWebOptions.Referer);
        request.Headers.Add("Cookie", snapshot.Credentials.CookieHeader);
        if (includeAuthUser) request.Headers.Add("X-Goog-AuthUser", (_options.AuthUser ?? 0).ToString());
    }

    internal void ApplyAuthenticatedHeaders(HttpRequestMessage request, YouTubeAuthenticatedSession session,
        bool includeRatingHeaders)
    {
        request.Headers.UserAgent.ParseAdd(YouTubeWebOptions.UserAgent);
        request.Headers.Add("Origin", YouTubeWebOptions.Origin);
        request.Headers.Add("Referer", YouTubeWebOptions.Referer);
        request.Headers.Add("X-Origin", YouTubeWebOptions.Origin);
        request.Headers.Add("Cookie", session.CredentialSnapshot.Credentials.CookieHeader);
        request.Headers.Add("X-Youtube-Client-Name", "1");
        request.Headers.Add("X-Youtube-Client-Version", session.Configuration.ClientVersion);
        if (!string.IsNullOrEmpty(session.Configuration.VisitorData))
            request.Headers.Add("X-Goog-Visitor-Id", session.Configuration.VisitorData);
        var timestamp = TimeSource();
        request.Headers.Authorization = new AuthenticationHeaderValue("SAPISIDHASH",
            $"{timestamp}_{session.CredentialSnapshot.Credentials.GenerateSapisidHash(timestamp)}");
        if (!includeRatingHeaders) return;
        request.Headers.Add("X-Goog-AuthUser", (_options.AuthUser ?? 0).ToString());
        request.Headers.Add("X-Youtube-Bootstrap-Logged-In", "true");
    }

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            _sessionVersion++;
            _cachedSession = null;
        }

        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal sealed record YouTubeCredentialSnapshot(YouTubeCredentials Credentials, long SessionVersion);

    internal sealed record YouTubeAuthenticatedSession(
        YouTubeCredentialSnapshot CredentialSnapshot,
        YouTubeBootstrapConfig Configuration);
}