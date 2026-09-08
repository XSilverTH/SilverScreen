using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Subscriptions;

namespace SilverScreen.Core.Account.Session;

/// <summary>
///     Single source of truth for the signed-in session predicate and every
///     session-gate message used by the feed and account surfaces.
///     Home, History, Subscriptions, and Account previously hand-rolled the same
///     "is there a usable signed-in session" check and copied the same
///     user-facing strings, so copy drifted (e.g. "session" vs "sign-in" wording
///     for the same expired sign-in state). All call sites delegate here:
///     <see cref="RequireSignedIn" /> is the only session predicate and the
///     constants below are the only message source.
///     The predicate is fail-closed: a null service never counts as signed in.
///     The presentation fallbacks keep their pre-migration wording so Wave 1
///     changes no user-visible text; unifying them with the plain-language gate
///     strings is a follow-up.
/// </summary>
public static class SessionGate
{
    public const string SignInActionLabel = "Sign In";

    public const string HomeSignedOutMessage = "Sign in with Google or use cookies.txt to see your Home feed.";
    public const string HistorySignedOutMessage = "Sign in with Google or use cookies.txt to see your watch history.";
    public const string SubscriptionsSignedOutMessage = "Sign in to YouTube to load your subscriptions.";

    public const string SessionNoLongerValidMessage = "Your YouTube sign-in is no longer valid.";
    public const string HomeLoadErrorMessage = "Could not load YouTube recommendations.";
    public const string HomeEmptyMessage = "No recommendations are available right now.";

    public const string HomeServiceAuthenticationRequiredMessage = "Sign in to YouTube to load recommendations.";
    public const string HistoryServiceAuthenticationRequiredMessage = "Sign in to YouTube to load your watch history.";
    public const string ServiceAuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";

    public const string HomePresentationSignedOutMessage = "Sign in to see your YouTube recommendations.";
    public const string HomePresentationAuthInvalidMessage = "Your YouTube session is no longer valid.";

    public const string HistorySignedOutTitle = "Sign in to see history";
    public const string HistoryPresentationSignedOutMessage = "Watch history requires an active YouTube session.";
    public const string HistoryErrorTitle = "Could not load history";
    public const string HistoryErrorMessage = "Failed to load your watch history. Check your network connection and try again.";
    public const string HistoryEmptyTitle = "No watch history";
    public const string HistoryEmptyMessage = "Videos you watch on YouTube will appear here.";

    public const string SubscriptionsSignedOutTitle = "Sign in to see subscriptions";
    public const string SubscriptionsPresentationSignedOutMessage = "Subscriptions feed requires an active YouTube session.";
    public const string SubscriptionsErrorTitle = "Could not load subscriptions";
    public const string SubscriptionsErrorMessage = "Failed to load your subscriptions. Check your network connection and try again.";
    public const string SubscriptionsEmptyTitle = "No subscriptions";
    public const string SubscriptionsEmptyMessage = "Channels you subscribe to on YouTube will appear here.";

    public static bool RequireSignedIn(ISessionService? sessionService)
    {
        if (sessionService is null)
            return false;

        var session = sessionService.GetCurrentSession();
        var cookies = sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    public static bool IsAuthInvalid(AuthenticatedHomeFeedStatus status)
    {
        return status is AuthenticatedHomeFeedStatus.AuthenticationRequired
            or AuthenticatedHomeFeedStatus.AuthenticationRejected;
    }

    public static bool IsAuthInvalid(AuthenticatedHistoryStatus status)
    {
        return status is AuthenticatedHistoryStatus.AuthenticationRequired
            or AuthenticatedHistoryStatus.AuthenticationRejected;
    }

    public static bool IsAuthInvalid(AuthenticatedSubscriptionsStatus status)
    {
        return status is AuthenticatedSubscriptionsStatus.AuthenticationRequired
            or AuthenticatedSubscriptionsStatus.AuthenticationRejected;
    }
}
