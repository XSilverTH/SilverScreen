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
/// Creates and caches YoutubeAPI clients by cookie snapshot. A new session gets a new client, while
/// requests sharing the same session reuse the client's bootstrapped InnerTube connection.
/// </summary>
public sealed class YouTubeClientProvider(ISessionService sessionService) : IYouTubeClientProvider, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YouTubeClientProvider>();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, YouTubeClient> _clients = new(StringComparer.Ordinal);
    private readonly ISessionService _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private bool _disposed;

    public YouTubeClient GetClient()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cookies = _sessionService.GetManualSessionCookies();
        var cookieContent = cookies?.Format == SessionCookieFormat.NetscapeCookiesText
            ? cookies.Content
            : string.Empty;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clients.TryGetValue(cookieContent, out var cached))
                return cached;

            var authentication = string.IsNullOrWhiteSpace(cookieContent)
                ? null
                : YouTubeCookieAuthentication.FromNetscape(cookieContent);
            var client = new YouTubeClient(new YouTubeClientOptions
            {
                Authentication = authentication
            });
            _clients.Add(cookieContent, client);
            Logger.Debug("Created YoutubeAPI client for {AuthenticationState} session",
                authentication is null ? "anonymous" : "authenticated");
            return client;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
        }
    }
}
