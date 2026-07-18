using System.Security.Cryptography;
using System.Text;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

public sealed class SecretServiceSessionService : ISessionService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly ICookieSecretStore _store;
    private readonly Lock _gate = new();
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
        }
        catch (SessionPersistenceException)
        {
            _manualCookies = null;
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
        {
            throw new ArgumentException("Manual session cookie content cannot be empty.", nameof(cookieContent));
        }

        var encodedCookies = Encode(cookieContent);
        try
        {
            lock (_gate)
            {
                _store.Save(encodedCookies);
                _manualCookies = new ManualSessionCookies(format, cookieContent);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encodedCookies);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        var changed = false;
        lock (_gate)
        {
            _store.Delete();
            changed = _manualCookies is not null;
            _manualCookies = null;
        }

        if (changed)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private ManualSessionCookies? LoadStoredCookies()
    {
        byte[]? encodedCookies = null;
        try
        {
            encodedCookies = _store.Load();
            if (encodedCookies is null)
            {
                return null;
            }

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
            if (encodedCookies is not null)
            {
                CryptographicOperations.ZeroMemory(encodedCookies);
            }
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
