using Serilog;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;

namespace SilverScreen.Infrastructure.Account.Profile;

/// <summary>Loads the authenticated YouTube account profile through YoutubeAPI.</summary>
public sealed class YoutubeApiAccountProfileService : IAccountProfileService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiAccountProfileService>();
    private readonly IYouTubeClientProvider _clientProvider;
    private readonly Lock _cacheGate = new();
    private readonly ISessionService _sessionService;
    private AccountProfile? _cachedProfile;
    private bool _disposed;

    public YoutubeApiAccountProfileService(
        IYouTubeClientProvider clientProvider,
        ISessionService sessionService)
    {
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public AccountProfile? GetCachedProfile()
    {
        if (!HasAuthenticatedSession())
            return null;

        lock (_cacheGate)
        {
            return _cachedProfile;
        }
    }

    public async Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!HasAuthenticatedSession())
        {
            Logger.Debug("Cannot fetch account profile without an authenticated YouTube session");
            return null;
        }

        try
        {
            Logger.Information("Fetching YouTube account profile");
            YoutubeAPI.Models.Account.Profile profile = await _clientProvider.GetClient().Account
                .GetProfileAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!HasAuthenticatedSession() || string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                Logger.Warning("YoutubeAPI returned no usable account profile or the session changed");
                return null;
            }

            var accountProfile = new AccountProfile(profile.DisplayName, profile.Avatar?.Url.ToString());
            lock (_cacheGate)
            {
                _cachedProfile = accountProfile;
            }

            Logger.Information("Account profile fetched successfully for {DisplayName}", accountProfile.DisplayName);
            return accountProfile;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to fetch account profile");
            return null;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure fetching account profile");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    private bool HasAuthenticatedSession()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } &&
               cookies is { Format: SessionCookieFormat.NetscapeCookiesText } &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private void OnSessionChanged(object? sender, EventArgs eventArgs)
    {
        lock (_cacheGate)
        {
            _cachedProfile = null;
        }
    }
}
