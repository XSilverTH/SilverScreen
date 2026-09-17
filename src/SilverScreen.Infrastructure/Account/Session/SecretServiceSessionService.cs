using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Browsing.Home;
using YoutubeAPI;

namespace SilverScreen.Infrastructure.Account.Session;

/// <summary>
///     Persists the manual YouTube session in Secret Service. Construction starts restoration
///     asynchronously and never waits for the keyring; session state is published when the
///     operation completes. All later keyring access is asynchronous as well.
/// </summary>
public sealed class SecretServiceSessionService : ISessionService, ISecretServiceAvailability, IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ILogger Logger = Log.ForContext<SecretServiceSessionService>();
    private readonly Func<IAuthenticatedHomeFeedService>? _feedServiceFactory;
    private readonly Lock _gate = new();
    private readonly Func<IAccountProfileService>? _profileServiceFactory;

    private readonly ICookieSecretStore _store;
    private readonly string? _tempRoot;
    private readonly string _signOutIntentPath;
    private readonly Task _restoreTask;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private bool _isAvailable = true;
    private bool _isValidating;
    private ManualSessionCookies? _manualCookies;
    private CancellationTokenSource? _validationCts;

    public SecretServiceSessionService(
        Func<IAccountProfileService>? profileServiceFactory,
        string? tempRoot = null)
        : this(new LibSecretCookieStore(), profileServiceFactory, null, tempRoot)
    {
    }

    internal SecretServiceSessionService(ICookieSecretStore store, string? tempRoot = null)
        : this(store, null, null, tempRoot)
    {
    }

    private SecretServiceSessionService(
        ICookieSecretStore store,
        Func<IAccountProfileService>? profileServiceFactory,
        Func<IAuthenticatedHomeFeedService>? feedServiceFactory,
        string? tempRoot = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _profileServiceFactory = profileServiceFactory;
        _feedServiceFactory = feedServiceFactory;
        _tempRoot = tempRoot;
        _signOutIntentPath = GetSignOutIntentPath(tempRoot);
        _restoreTask = Task.Run(RestoreStoredCookiesAsync);
    }

    public void Dispose()
    {
        CancelValidation();
    }

    internal Task WaitForRestoreAsync() => _restoreTask;

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
        IAccountProfileService? profileService;
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
            profileService = _profileServiceFactory?.Invoke();
            feedService = _feedServiceFactory?.Invoke();
        }

        try
        {
            if (profileService is not null)
            {
                var profile = await profileService.GetCurrentProfileAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                var result = new HomeSessionValidationResult(
                    profile is not null,
                    0,
                    false,
                    profile is null,
                    profile is null
                        ? AuthenticatedHomeFeedStatus.AuthenticationRejected
                        : AuthenticatedHomeFeedStatus.Success,
                    profile is null
                        ? "The YouTube session was rejected or has expired."
                        : "Account profile loaded.");
                return SessionValidationFormatter.FormatResult(result);
            }

            if (feedService is null)
                return SessionValidationFormatter.FormatUnexpectedError();

            var feedResult = await feedService.LoadFirstPageAsync(cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);
            var isSuccess = feedResult.Status == AuthenticatedHomeFeedStatus.Success;
            var videoCount = feedResult.FeedPage.Videos.Count;
            var hasContinuation = !string.IsNullOrEmpty(feedResult.FeedPage.ContinuationToken);
            var requiresAuth = feedResult.Status is AuthenticatedHomeFeedStatus.AuthenticationRequired
                or AuthenticatedHomeFeedStatus.AuthenticationRejected;

            var homeResult = new HomeSessionValidationResult(
                isSuccess,
                videoCount,
                hasContinuation,
                requiresAuth,
                feedResult.Status,
                feedResult.StatusMessage);
            return SessionValidationFormatter.FormatResult(homeResult);
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
        SetManualSessionAsync(cookieContent, format).GetAwaiter().GetResult();
    }
    public async Task SetManualSessionAsync(string cookieContent, SessionCookieFormat format)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            throw new ArgumentException("Manual session cookie content cannot be empty.", nameof(cookieContent));

        await _restoreTask.ConfigureAwait(false);
        await _persistenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var encodedCookies = Encode(cookieContent);
            try
            {
                await _store.SaveAsync(encodedCookies).ConfigureAwait(false);
                ClearSignOutIntent();
                lock (_gate)
                {
                    _isAvailable = true;
                    _manualCookies = new ManualSessionCookies(format, cookieContent);
                }

                Logger.Information("Successfully persisted YouTube session to Secret Service (Format: {Format}", format);
                SessionChanged?.Invoke(this, EventArgs.Empty);
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
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public void ClearSession()
    {
        ClearSessionAsync().GetAwaiter().GetResult();
    }

    public async Task ClearSessionAsync()
    {
        await _restoreTask.ConfigureAwait(false);
        await _persistenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelValidation();
            bool changed;
            PersistSignOutIntent();
            try
            {
                await _store.DeleteAsync().ConfigureAwait(false);
                ClearSignOutIntent();
                lock (_gate)
                {
                    _isAvailable = true;
                    changed = _manualCookies is not null;
                    _manualCookies = null;
                }

                Logger.Information("Cleared YouTube session and secret store");
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _isAvailable = false;
                    changed = _manualCookies is not null;
                    _manualCookies = null;
                }

                Logger.Warning(ex,
                    "Failed to clear YouTube session in Secret Service; recovering local sign-out state and retaining sign-out intent for retry");
            }

            if (changed) SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private async Task RestoreStoredCookiesAsync()
    {
        try
        {
            if (File.Exists(_signOutIntentPath))
            {
                lock (_gate)
                {
                    _manualCookies = null;
                    _isAvailable = true;
                }

                Logger.Information("Skipping YouTube session restoration because a pending sign-out intent exists");
                SessionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var restoredCookies = await LoadStoredCookiesAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _manualCookies = restoredCookies;
                _isAvailable = true;
            }

            Logger.Information("YouTube session state in Secret Service: {SessionState}",
                restoredCookies is not null ? "Restored" : "Not found");
        }
        catch (Exception exception)
        {
            Logger.Warning("Secret Service was unavailable while restoring the YouTube session: {Message}",
                DiagnosticSanitizer.Sanitize(exception.InnerException?.Message ?? exception.Message));
            Logger.Debug("Secret Service startup restoration error details: {Details}",
                DiagnosticSanitizer.Sanitize(exception.ToString()));
            lock (_gate)
            {
                _isAvailable = false;
                _manualCookies = null;
            }
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task<ManualSessionCookies?> LoadStoredCookiesAsync()
    {
        byte[]? encodedCookies = null;
        try
        {
            encodedCookies = await _store.LoadAsync().ConfigureAwait(false);
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

            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                if (!YouTubeCookieAuthentication.FromNetscape(content).HasAuthenticationCookies)
                {
                    Logger.Warning("Stored YouTube session contains no authentication cookies; ignoring it");
                    return null;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                Logger.Warning(exception, "Stored YouTube session cookies are invalid; ignoring them");
                return null;
            }

            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, content);
        }
        finally
        {
            if (encodedCookies is not null) CryptographicOperations.ZeroMemory(encodedCookies);
        }
    }

    private void PersistSignOutIntent()
    {
        try
        {
            var directory = Path.GetDirectoryName(_signOutIntentPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory ?? Directory.GetCurrentDirectory(),
                $".{Path.GetFileName(_signOutIntentPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64,
                           FileOptions.WriteThrough))
                {
                    stream.WriteByte((byte)'1');
                    stream.Flush(true);
                }

                File.Move(temporaryPath, _signOutIntentPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception,
                "Could not persist the pending sign-out intent; keyring deletion will still be attempted");
        }
    }

    private void ClearSignOutIntent()
    {
        try
        {
            File.Delete(_signOutIntentPath);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not clear the persisted sign-out intent");
        }
    }

    private static string GetSignOutIntentPath(string? tempRoot)
    {
        if (!string.IsNullOrWhiteSpace(tempRoot))
            return Path.Combine(tempRoot, ".silverscreen-signed-out");

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configHome = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, ".config");
        }

        return Path.Combine(configHome, "SilverScreen", "sign-out-intent");
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