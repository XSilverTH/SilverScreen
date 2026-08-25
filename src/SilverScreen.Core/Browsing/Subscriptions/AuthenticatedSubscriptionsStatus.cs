namespace SilverScreen.Core.Browsing.Subscriptions;

public enum AuthenticatedSubscriptionsStatus
{
    Success,
    AuthenticationRequired,
    AuthenticationRejected,
    TemporaryBackendFailure,
    Empty
}
