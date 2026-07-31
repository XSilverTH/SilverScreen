namespace SilverScreen.Core.Models;

public enum AuthenticatedHistoryStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryBackendFailure,
    Empty
}
