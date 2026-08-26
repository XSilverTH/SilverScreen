using SilverScreen.Core.Browsing.Home;

namespace SilverScreen.Infrastructure.Account.Session;

public static class SessionValidationFormatter
{
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