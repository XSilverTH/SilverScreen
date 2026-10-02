using Serilog;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;

namespace SilverScreen.Infrastructure.Account.Profile;

/// <summary>Loads the authenticated YouTube account profile through YoutubeAPI.</summary>
public sealed class YoutubeApiAccountProfileService : IAccountProfileService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiAccountProfileService>();
    private readonly Lock _cacheGate = new();
    private readonly IYouTubeClientProvider _clientProvider;
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
            var profile = await _clientProvider.GetAuthenticatedClient().Account
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

            Logger.Information("YouTube account profile fetched successfully");
            return accountProfile;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            LogProfileFailure(exception, ClassifyFailure(exception));
            return null;
        }
        catch (Exception exception)
        {
            LogProfileFailure(exception, exception is HttpRequestException ? "request" : "unexpected");
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
    private void LogProfileFailure(Exception exception, string category)
    {
        Logger.Warning(
            "YouTube account profile request failed ({FailureCategory}; {ExceptionClass}). Stored session locally present: {StoredSessionPresent}. Session is retained; no automatic logout was performed. Failure details: {FailureDetails}",
            category, exception.GetType().Name, HasAuthenticatedSession(),
            DiagnosticSanitizer.Sanitize(exception.ToString()));
    }

    private static string ClassifyFailure(YouTubeException exception)
    {
        return exception switch
        {
            AuthenticationRequiredException or AuthenticationExpiredException => "authentication rejection",
            YouTubeProtocolException => "protocol",
            YouTubeRequestException => "request",
            _ => "YouTube API"
        };
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