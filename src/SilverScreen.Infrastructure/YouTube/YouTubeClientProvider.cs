using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;
using YoutubeAPI;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>Provides YoutubeAPI clients configured for the current SilverScreen session.</summary>
public interface IYouTubeClientProvider
{
    YouTubeClient GetClient();
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cookies = _sessionService.GetManualSessionCookies();
        var cookieContent = cookies?.Format == SessionCookieFormat.NetscapeCookiesText
            ? cookies.Content
            : string.Empty;
        var sessionKey = HashSessionCookies(cookieContent);

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

            YouTubeCookieAuthentication? authentication = null;
            if (!string.IsNullOrWhiteSpace(cookieContent))
                try
                {
                    authentication = YouTubeCookieAuthentication.FromNetscape(cookieContent);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException)
                {
                    Logger.Warning(exception,
                        "Stored session cookies are invalid or corrupted; falling back to unauthenticated client");
                    authentication = null;
                }

            client = new YouTubeClient(new YouTubeClientOptions
            {
                Authentication = authentication
            });
            _clients.Add(sessionKey, _lru.AddLast(new CachedClient(sessionKey, client)));
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