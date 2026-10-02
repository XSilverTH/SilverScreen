using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;
using YoutubeAPI;
using YoutubeAPI.Exceptions;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>Provides YoutubeAPI clients configured for the current SilverScreen session.</summary>
public interface IYouTubeClientProvider
{
    YouTubeClient GetClient();

    YouTubeClient GetAuthenticatedClient();
}

/// <summary>
///     Creates and caches YoutubeAPI clients by session-cookie hash. A new session gets a new client, while
///     requests sharing the same session reuse the client's bootstrapped InnerTube connection.
///     At most two clients are cached (least-recently-used eviction); evicted clients are disposed.
///     Cache keys are SHA256 hashes of the cookie content: raw cookies never appear as keys or in logs,
///     and only a truncated hash prefix is logged for diagnostics.
/// </summary>
public sealed class YouTubeClientProvider(ISessionService sessionService) : IYouTubeClientProvider, IDisposable
{
    private const int MaxCachedClients = 2;
    private const int LoggedHashPrefixLength = 12;

    private static readonly HashSet<string> DiagnosticCookieNames = new(StringComparer.Ordinal)
    {
        "SAPISID",
        "__Secure-3PAPISID",
        "APISID",
        "__Secure-1PAPISID",
        "SID",
        "HSID",
        "SSID",
        "LOGIN_INFO",
        "__Secure-1PSID",
        "__Secure-3PSID",
        "__Secure-1PSIDTS",
        "__Secure-3PSIDTS",
        "SIDCC",
        "__Secure-1PSIDCC",
        "__Secure-3PSIDCC"
    };

    private static readonly ILogger Logger = Log.ForContext<YouTubeClientProvider>();
    private readonly Dictionary<string, LinkedListNode<CachedClient>> _clients = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly LinkedList<CachedClient> _lru = new();

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private bool _disposed;

    public void Dispose()
    {
        var evicted = new List<YouTubeClient>();
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            evicted.AddRange(_lru.Select(entry => entry.Client));
            _clients.Clear();
            _lru.Clear();
        }

        foreach (var client in evicted)
            client.Dispose();
    }

    public YouTubeClient GetClient()
    {
        return GetClient(requireAuthentication: false);
    }

    public YouTubeClient GetAuthenticatedClient()
    {
        return GetClient(requireAuthentication: true);
    }

    private YouTubeClient GetClient(bool requireAuthentication)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cookies = _sessionService.GetManualSessionCookies();
        var cookieContent = cookies?.Format == SessionCookieFormat.NetscapeCookiesText
            ? cookies.Content
            : string.Empty;
        var sessionKey = HashSessionCookies(cookieContent);

        YouTubeCookieAuthentication? authentication = null;
        if (!string.IsNullOrWhiteSpace(cookieContent))
            try
            {
                authentication = YouTubeCookieAuthentication.FromNetscape(cookieContent);
                if (!authentication.HasAuthenticationCookies)
                {
                    if (requireAuthentication)
                        throw new AuthenticationRequiredException(
                            "Stored YouTube session does not contain authentication cookies.");

                    authentication = null;
                }
            }
            catch (AuthenticationRequiredException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                if (requireAuthentication)
                    throw new AuthenticationRequiredException(
                        "Stored YouTube session cookies are invalid or corrupted.", exception);

                Logger.Warning(exception,
                    "Stored session cookies are invalid or corrupted; falling back to unauthenticated client");
                authentication = null;
            }
        else if (requireAuthentication)
        {
            throw new AuthenticationRequiredException(
                "This operation requires a valid authenticated YouTube session.");
        }

        List<YouTubeClient>? evicted = null;
        YouTubeClient client;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clients.TryGetValue(sessionKey, out var hit))
            {
                _lru.Remove(hit);
                _lru.AddLast(hit);
                return hit.Value.Client;
            }

            client = new YouTubeClient(new YouTubeClientOptions
            {
                Authentication = authentication
            });
            _clients.Add(sessionKey, _lru.AddLast(new CachedClient(sessionKey, client)));
            if (authentication is not null)
                LogImportedCookieMetadata(cookieContent, authentication);
            Logger.Debug("Created YoutubeAPI client for {AuthenticationState} session ({SessionHash})",
                authentication is null ? "anonymous" : "authenticated",
                TruncateHash(sessionKey));

            while (_clients.Count > MaxCachedClients && _lru.First is not null)
            {
                var oldest = _lru.First;
                _lru.RemoveFirst();
                _clients.Remove(oldest.Value.SessionKey);
                evicted ??= [];
                evicted.Add(oldest.Value.Client);
                Logger.Debug("Evicted YoutubeAPI client ({SessionHash})", TruncateHash(oldest.Value.SessionKey));
            }
        }

        if (evicted is null) return client;
        foreach (var evictedClient in evicted)
            evictedClient.Dispose();

        return client;
    }

    private static void LogImportedCookieMetadata(
        string cookieContent,
        YouTubeCookieAuthentication authentication)
    {
        var parsedCookies = authentication.Cookies;
        var retainedByNameAndExpiry = new Dictionary<(string Name, long ExpiryUnix), int>();
        var retainedNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var retainedAllowlistedCount = 0;
        foreach (var cookie in parsedCookies)
        {
            var expiryUnix = cookie.Expires == DateTime.MinValue
                ? 0
                : new DateTimeOffset(DateTime.SpecifyKind(cookie.Expires, DateTimeKind.Utc)).ToUnixTimeSeconds();
            if (!DiagnosticCookieNames.Contains(cookie.Name))
                continue;

            retainedAllowlistedCount++;
            var key = (cookie.Name, expiryUnix);
            retainedByNameAndExpiry[key] = retainedByNameAndExpiry.GetValueOrDefault(key) + 1;
            retainedNameCounts[cookie.Name] = retainedNameCounts.GetValueOrDefault(cookie.Name) + 1;
        }

        var rows = new List<(string Name, long ExpiryUnix)>();
        var rawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownCount = 0;
        using (var reader = new StringReader(cookieContent))
        {
            while (reader.ReadLine() is { } rawLine)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
                    line = line["#HttpOnly_".Length..];
                else if (line[0] == '#')
                    continue;

                var fields = line.Split('\t');
                if (fields.Length != 7)
                    fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 7 || !long.TryParse(fields[4],
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var expiryUnix))
                    continue;

                var name = fields[5];
                if (!DiagnosticCookieNames.Contains(name))
                {
                    unknownCount++;
                    continue;
                }

                rows.Add((name, expiryUnix > 0 ? expiryUnix : 0));
                rawCounts[name] = rawCounts.GetValueOrDefault(name) + 1;
            }
        }

        var expiryCounts = new Dictionary<string, (int Sessions, int Expired)>(StringComparer.Ordinal);
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var row in rows)
        {
            var rawCount = rawCounts[row.Name];
            var key = (row.Name, row.ExpiryUnix);
            var retained = retainedByNameAndExpiry.GetValueOrDefault(key) > 0;
            if (retained)
                retainedByNameAndExpiry[key]--;

            if (row.ExpiryUnix <= 0)
            {
                var counts = expiryCounts.GetValueOrDefault(row.Name);
                expiryCounts[row.Name] = (counts.Sessions + 1, counts.Expired);
                Logger.Information(
                    "Imported YouTube cookie metadata: {CookieName}, raw count {RawCount}, retained count {RetainedCount}, expiry UTC session, session {IsSessionCookie}, expired {IsExpired}, retained at import {RetainedAtImport}",
                    row.Name, rawCount, retainedNameCounts.GetValueOrDefault(row.Name), true, false, retained);
                continue;
            }

            try
            {
                var expiresUtc = DateTimeOffset.FromUnixTimeSeconds(row.ExpiryUnix);
                var isExpired = row.ExpiryUnix < nowUnix;
                var counts = expiryCounts.GetValueOrDefault(row.Name);
                expiryCounts[row.Name] = (counts.Sessions, counts.Expired + (isExpired ? 1 : 0));
                Logger.Information(
                    "Imported YouTube cookie metadata: {CookieName}, raw count {RawCount}, retained count {RetainedCount}, expiry UTC {ExpiryUtc}, session {IsSessionCookie}, expired {IsExpired}, retained at import {RetainedAtImport}",
                    row.Name, rawCount, retainedNameCounts.GetValueOrDefault(row.Name), expiresUtc, false, isExpired, retained);
            }
            catch (ArgumentOutOfRangeException)
            {
                Logger.Information(
                    "Imported YouTube cookie metadata: {CookieName}, raw count {RawCount}, retained count {RetainedCount}, expiry unavailable, session {IsSessionCookie}, expired unavailable, retained at import {RetainedAtImport}",
                    row.Name, rawCount, retainedNameCounts.GetValueOrDefault(row.Name), false, false, retained);
            }
        }

        foreach (var (name, counts) in expiryCounts)
            Logger.Information(
                "Imported YouTube cookie expiry summary: {CookieName}, session count {SessionCount}, expired count {ExpiredCount}",
                name, counts.Sessions, counts.Expired);

        Logger.Information(
            "Imported YouTube unknown-cookie metadata: raw count {RawCount}, retained count {RetainedCount}",
            unknownCount, Math.Max(0, parsedCookies.Count - retainedAllowlistedCount));
    }


    private static string HashSessionCookies(string cookieContent)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookieContent)));
    }

    private static string TruncateHash(string sessionKey)
    {
        return sessionKey.Length > LoggedHashPrefixLength ? sessionKey[..LoggedHashPrefixLength] : sessionKey;
    }

    private sealed record CachedClient(string SessionKey, YouTubeClient Client);
}