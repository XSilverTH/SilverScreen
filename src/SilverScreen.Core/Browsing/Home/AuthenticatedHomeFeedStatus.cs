namespace SilverScreen.Core.Browsing.Home;

public enum AuthenticatedHomeFeedStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryBackendFailure,
    Empty
}