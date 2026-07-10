using System;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Feed;

namespace SilverScreen.Features.Session;

public sealed class SessionValidationCoordinator : IDisposable
{
    private readonly HomeSessionValidator _validator;
    private readonly ISessionService _sessionService;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _isValidating;

    public SessionValidationCoordinator(HomeSessionValidator validator, ISessionService sessionService)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    }

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

    public async Task<string> ValidateAsync()
    {
        CancellationToken token;
        lock (_lock)
        {
            if (!HasManualSession())
            {
                return SessionValidationFormatter.NoActiveSessionMessage;
            }

            if (_isValidating)
            {
                return SessionValidationFormatter.AlreadyRunningMessage;
            }

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
            if (_isValidating && _cts != null)
            {
                _cts.Cancel();
            }
        }
    }

    private bool HasManualSession()
    {
        var session = _sessionService.GetCurrentSession();
        return session.IsSignedIn && session.HasManualSession;
    }

    public void Dispose()
    {
        Cancel();
    }
}

public static class SessionValidationFormatter
{
    public const string ValidatingMessage = "Validating YouTube session…";
    public const string CancellationMessage = "Validation canceled.";
    public const string UnexpectedErrorMessage = "Validation failed: An unexpected error occurred.";
    public const string NoActiveSessionMessage = "Validation failed: No manual YouTube session is active.";
    public const string AlreadyRunningMessage = "Validation is already in progress.";

    public static string FormatResult(HomeSessionValidationResult result)
    {
        return $"Validation {(result.IsSuccess ? "succeeded" : "failed")}. Usable videos: {result.VideoCount}. Continuation available: {(result.HasContinuation ? "yes" : "no")}. Authentication required: {(result.RequiresAuthentication ? "yes" : "no")}. Status: {FormatHighLevelStatus(result.HighLevelStatus)}";
    }

    private static string FormatHighLevelStatus(AuthenticatedHomeFeedStatus status)
    {
        return status switch
        {
            AuthenticatedHomeFeedStatus.Success => "Recommendations loaded.",
            AuthenticatedHomeFeedStatus.AuthenticationRequired => "A manual YouTube session is required.",
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
