using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

public sealed class SecretServiceSessionService : ISessionService, ISecretServiceAvailability
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ILogger Logger = Log.ForContext<SecretServiceSessionService>();
    private readonly Lock _gate = new();

    private readonly ICookieSecretStore _store;
    private bool _isAvailable = true;
    private ManualSessionCookies? _manualCookies;

    public SecretServiceSessionService()
        : this(new LibSecretCookieStore())
    {
    }

    internal SecretServiceSessionService(ICookieSecretStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        try
        {
            _manualCookies = LoadStoredCookies();
            if (_manualCookies is not null)
            {
                Logger.Information("Restored YouTube session from Secret Service");
            }
            else
            {
                Logger.Information("No stored YouTube session found in Secret Service");
            }
        }
        catch (SessionPersistenceException exception)
        {
            Logger.Warning(exception, "Secret Service was unavailable while restoring the YouTube session");
            _isAvailable = false;
            _manualCookies = null;
        }
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
                Logger.Information("Successfully persisted YouTube session to Secret Service (Format: {Format})", format);
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