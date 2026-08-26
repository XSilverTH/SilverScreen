using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Home;

namespace SilverScreen.Infrastructure.Account.Session;

public sealed class SecretServiceSessionService : ISessionService, ISecretServiceAvailability, IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ILogger Logger = Log.ForContext<SecretServiceSessionService>();
    private readonly Func<IAuthenticatedHomeFeedService>? _feedServiceFactory;
    private readonly Lock _gate = new();

    private readonly ICookieSecretStore _store;
    private readonly string? _tempRoot;
    private bool _isAvailable = true;
    private bool _isValidating;
    private ManualSessionCookies? _manualCookies;
    private CancellationTokenSource? _validationCts;

    public SecretServiceSessionService(Func<IAuthenticatedHomeFeedService>? feedServiceFactory, string? tempRoot = null)
        : this(new LibSecretCookieStore(), feedServiceFactory, tempRoot)
    {
    }

    internal SecretServiceSessionService(ICookieSecretStore store, string? tempRoot = null)
        : this(store, null, tempRoot)
    {
    }

    private SecretServiceSessionService(
        ICookieSecretStore store,
        Func<IAuthenticatedHomeFeedService>? feedServiceFactory,
        string? tempRoot = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _feedServiceFactory = feedServiceFactory;
        _tempRoot = tempRoot;
        try
        {
            _manualCookies = LoadStoredCookies();
            Logger.Information("YouTube session state in Secret Service: {SessionState}",
                _manualCookies is not null ? "Restored" : "Not found");
        }
        catch (SessionPersistenceException exception)
        {
            Logger.Warning("Secret Service was unavailable while restoring the YouTube session: {Message}",
                exception.InnerException?.Message ?? exception.Message);
            Logger.Debug(exception, "Secret Service startup restoration error details");
            _isAvailable = false;
            _manualCookies = null;
        }
    }

    public bool IsValidating
    {
        get
        {
            lock (_gate)
            {
                return _isValidating;
            }
        }
    }

    public void Dispose()
    {
        CancelValidation();
    }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _isAvailable;
            }
        }
    }

    public event EventHandler? SessionChanged;

    public AccountSession GetCurrentSession()
    {
        lock (_gate)
        {
            return _manualCookies is null
                ? AccountSession.SignedOut
                : new AccountSession(
                    true,
                    "YouTube session",
                    HasManualSession: true,
                    CookieFormat: _manualCookies.Format);
        }
    }

    public ManualSessionCookies? GetManualSessionCookies()
    {
        lock (_gate)
        {
            return _manualCookies;
        }
    }

    public CookieFileLease? AcquireCookieFileLease()
    {
        lock (_gate)
        {
            if (_manualCookies is null || _manualCookies.Format != SessionCookieFormat.NetscapeCookiesText ||
                string.IsNullOrWhiteSpace(_manualCookies.Content))
                return null;

            return TemporaryCookieFile.CreateLease(_manualCookies.Content, _tempRoot);
        }
    }

    public CookieFileLease? CreateCookieFile()
    {
        return AcquireCookieFileLease();
    }

    public CookieContainer? CreateCookieContainer()
    {
        lock (_gate)
        {
            if (_manualCookies is null || _manualCookies.Format != SessionCookieFormat.NetscapeCookiesText ||
                string.IsNullOrWhiteSpace(_manualCookies.Content))
                return null;

            return NetscapeCookieParser.CreateCookieContainer(_manualCookies.Content);
        }
    }

    public async Task<string> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Starting YouTube session validation");
        CancellationTokenSource linkedCts;
        IAuthenticatedHomeFeedService? feedService;
        lock (_gate)
        {
            var session = GetCurrentSession();
            if (!session.IsSignedIn || !session.HasManualSession)
                return SessionValidationFormatter.NoActiveSessionMessage;

            if (_isValidating)
                return SessionValidationFormatter.AlreadyRunningMessage;

            _isValidating = true;
            _validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _validationCts;
            feedService = _feedServiceFactory?.Invoke();
        }

        if (feedService is null)
        {
            lock (_gate)
            {
                _isValidating = false;
                _validationCts?.Dispose();
                _validationCts = null;
            }

            return SessionValidationFormatter.FormatUnexpectedError();
        }

        try
        {
            var feedResult = await feedService.LoadFirstPageAsync(cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);
            var isSuccess = feedResult.Status == AuthenticatedHomeFeedStatus.Success;
            var videoCount = feedResult.FeedPage.Videos.Count;
            var hasContinuation = !string.IsNullOrEmpty(feedResult.FeedPage.ContinuationToken);
            var requiresAuth = feedResult.Status is AuthenticatedHomeFeedStatus.AuthenticationRequired
                or AuthenticatedHomeFeedStatus.AuthenticationRejected;

            var result = new HomeSessionValidationResult(
                isSuccess,
                videoCount,
                hasContinuation,
                requiresAuth,
                feedResult.Status,
                feedResult.StatusMessage);
            return SessionValidationFormatter.FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            return SessionValidationFormatter.FormatCancellation();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unexpected error during session validation");
            return SessionValidationFormatter.FormatUnexpectedError();
        }
        finally
        {
            lock (_gate)
            {
                _isValidating = false;
                _validationCts?.Dispose();
                _validationCts = null;
            }
        }
    }

    public void CancelValidation()
    {
        lock (_gate)
        {
            if (_isValidating && _validationCts != null)
                _validationCts.Cancel();
        }
    }


    public void SetManualSession(string cookieContent, SessionCookieFormat format)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            throw new ArgumentException("Manual session cookie content cannot be empty.", nameof(cookieContent));

        var encodedCookies = Encode(cookieContent);
        try
        {
            lock (_gate)
            {
                _store.Save(encodedCookies);
                _isAvailable = true;
                _manualCookies = new ManualSessionCookies(format, cookieContent);
                Logger.Information("Successfully persisted YouTube session to Secret Service (Format: {Format})",
                    format);
            }
        }
        catch (SessionPersistenceException ex)
        {
            Logger.Error(ex, "Failed to persist YouTube session to Secret Service");
            lock (_gate)
            {
                _isAvailable = false;
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedCookies);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        CancelValidation();
        bool changed;
        try
        {
            lock (_gate)
            {
                _store.Delete();
                _isAvailable = true;
                changed = _manualCookies is not null;
                _manualCookies = null;
                Logger.Information("Cleared YouTube session and secret store");
            }
        }
        catch (SessionPersistenceException ex)
        {
            Logger.Error(ex, "Failed to clear YouTube session in Secret Service");
            lock (_gate)
            {
                _isAvailable = false;
            }

            throw;
        }

        if (changed) SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private ManualSessionCookies? LoadStoredCookies()
    {
        byte[]? encodedCookies = null;
        try
        {
            encodedCookies = _store.Load();
            if (encodedCookies is null) return null;

            string content;
            try
            {
                content = StrictUtf8.GetString(encodedCookies);
            }
            catch (DecoderFallbackException)
            {
                throw new SessionPersistenceException();
            }

            return string.IsNullOrWhiteSpace(content)
                ? null
                : new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, content);
        }
        finally
        {
            if (encodedCookies is not null) CryptographicOperations.ZeroMemory(encodedCookies);
        }
    }

    private static byte[] Encode(string cookieContent)
    {
        try
        {
            return StrictUtf8.GetBytes(cookieContent);
        }
        catch (EncoderFallbackException)
        {
            throw new SessionPersistenceException();
        }
    }
}