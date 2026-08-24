namespace SilverScreen.Core.Browsing.History;

public enum AuthenticatedHistoryStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryBackendFailure,
    Empty
}