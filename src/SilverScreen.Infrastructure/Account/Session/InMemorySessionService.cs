using System.Net;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Home;

namespace SilverScreen.Infrastructure.Account.Session;

public sealed class InMemorySessionService(
    Func<IAuthenticatedHomeFeedService>? feedServiceFactory,
    string? tempRoot = null)
    : ISessionService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<InMemorySessionService>();
    private readonly Lock _gate = new();
    private bool _isValidating;
    private ManualSessionCookies? _manualCookies;
    private CancellationTokenSource? _validationCts;

    public InMemorySessionService(string? tempRoot = null)
        : this((Func<IAuthenticatedHomeFeedService>?)null, tempRoot)
    {
    }

    public InMemorySessionService(IAuthenticatedHomeFeedService feedService, string? tempRoot = null)
        : this(() => feedService, tempRoot)
    {
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

            return TemporaryCookieFile.CreateLease(_manualCookies.Content, tempRoot);
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
            feedService = feedServiceFactory?.Invoke();
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

        lock (_gate)
        {
            _manualCookies = new ManualSessionCookies(format, cookieContent);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        CancelValidation();
        bool changed;
        lock (_gate)
        {
            changed = _manualCookies is not null;
            _manualCookies = null;
        }

        if (changed)
            SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}