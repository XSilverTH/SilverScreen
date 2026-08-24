using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;

namespace SilverScreen.Account.Session;

public sealed class SessionValidationCoordinator(IAuthenticatedHomeFeedService feedService, ISessionService sessionService)
    : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SessionValidationCoordinator>();
    private readonly Lock _lock = new();

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private readonly IAuthenticatedHomeFeedService _feedService =
        feedService ?? throw new ArgumentNullException(nameof(feedService));
    private CancellationTokenSource? _cts;
    private bool _isValidating;
    public bool IsValidating
    {
        get
        {
            lock (_lock)
            {
                return _isValidating;
            }
        }
    }

    public bool IsAvailable => HasManualSession() && !IsValidating;

    public void Dispose()
    {
        Cancel();
    }

    public async Task<string> ValidateAsync()
    {
        Logger.Information("Starting YouTube session validation");
        CancellationToken token;
        lock (_lock)
        {
            if (!HasManualSession()) return SessionValidationFormatter.NoActiveSessionMessage;

            if (_isValidating) return SessionValidationFormatter.AlreadyRunningMessage;

            _isValidating = true;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        try
        {
            var feedResult = await _feedService.LoadFirstPageAsync(token);
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
            lock (_lock)
            {
                _isValidating = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (_isValidating && _cts != null) _cts.Cancel();
        }
    }

    private bool HasManualSession()
    {
        var session = _sessionService.GetCurrentSession();
        return session is { IsSignedIn: true, HasManualSession: true };
    }
}

public static class SessionValidationFormatter
{
    public const string ValidatingMessage = "Validating YouTube session…";
    private const string CancellationMessage = "Validation canceled.";
    private const string UnexpectedErrorMessage = "Validation failed: An unexpected error occurred.";
    public const string NoActiveSessionMessage = "Validation failed: No YouTube session is active.";
    public const string AlreadyRunningMessage = "Validation is already in progress.";

    public static string FormatResult(HomeSessionValidationResult result)
    {
        return
            $"Validation {(result.IsSuccess ? "succeeded" : "failed")}. Usable videos: {result.VideoCount}. Continuation available: {(result.HasContinuation ? "yes" : "no")}. Authentication required: {(result.RequiresAuthentication ? "yes" : "no")}. Status: {FormatHighLevelStatus(result.HighLevelStatus)}";
    }

    private static string FormatHighLevelStatus(AuthenticatedHomeFeedStatus status)
    {
        return status switch
        {
            AuthenticatedHomeFeedStatus.Success => "Recommendations loaded.",
            AuthenticatedHomeFeedStatus.AuthenticationRequired => "A YouTube session is required.",
            AuthenticatedHomeFeedStatus.AuthenticationRejected => "The YouTube session was rejected or has expired.",
            AuthenticatedHomeFeedStatus.TemporaryBackendFailure => "Recommendations are temporarily unavailable.",
            AuthenticatedHomeFeedStatus.Empty => "No usable recommendations were returned.",
            _ => "Validation returned an unknown status."
        };
    }

    public static string FormatCancellation()
    {
        return CancellationMessage;
    }

    public static string FormatUnexpectedError()
    {
        return UnexpectedErrorMessage;
    }
}