using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;

namespace SilverScreen.Infrastructure.Features.Session;

public sealed class SessionValidationCoordinator(HomeSessionValidator validator, ISessionService sessionService)
    : IDisposable
{
    private readonly Lock _lock = new();

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private readonly HomeSessionValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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
            var result = await _validator.ValidateSessionAsync(token);
            return SessionValidationFormatter.FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            return SessionValidationFormatter.FormatCancellation();
        }
        catch (Exception)
        {
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
        return session.IsSignedIn && session.HasManualSession;
    }
}

public static class SessionValidationFormatter
{
    public const string ValidatingMessage = "Validating YouTube session…";
    public const string CancellationMessage = "Validation canceled.";
    public const string UnexpectedErrorMessage = "Validation failed: An unexpected error occurred.";
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