namespace SilverScreen.Core.Models;

public enum AuthenticatedHomeFeedStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryBackendFailure,
    Empty
}
