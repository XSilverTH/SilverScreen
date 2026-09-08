using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Home;

namespace SilverScreen.Features;

/// <summary>
///     Shared session-gate vocabulary for feed and account surfaces.
///     Home, History, and Account previously hand-rolled the same "is there a
///     usable signed-in session" check and copied the same user-facing strings,
///     so copy drifted (e.g. "session" vs "sign-in" wording for the same expired
///     sign-in state). The Wave 1 plain-language strings live here as the single
///     source of truth: signed-out vs error vs ok.
///     Batch scope: HomeFeedCoordinator and AccountViewModel consume this helper.
///     HistoryViewModel, SubscriptionsViewModel, and the YoutubeApi* services
///     still carry their own copies and adopt this helper in a follow-up; the
///     HomeVideoListSource.MapState wrapper likewise keeps its older copy until
///     its owner aligns it (Wave 1 uncertainty, unchanged here).
/// </summary>
public enum SessionGateState
{
    SignedIn,
    SignedOut
}

public static class SessionGate
{
    private const string SignInActionLabel = "Sign In";
    private const string HomeSignedOutMessage = "Sign in with Google or use cookies.txt to see your Home feed.";
    public const string HistorySignedOutMessage = "Sign in with Google or use cookies.txt to see your watch history.";
    public const string SessionNoLongerValidMessage = "Your YouTube sign-in is no longer valid.";
    public const string HomeLoadErrorMessage = "Could not load YouTube recommendations.";
    private const string HomeEmptyMessage = "No recommendations are available right now.";

    public static bool RequireSignedIn(ISessionService? sessionService)
    {
        if (sessionService is null)
            return true;

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

    public static VideoListStatus HomeSignedOutStatus(Action? openWebLogin)
    {
        return new VideoListStatus(
            "Home",
            HomeSignedOutMessage,
            "avatar-default-symbolic",
            false,
            openWebLogin is null ? null : SignInActionLabel,
            openWebLogin);
    }

    public static VideoListStatus HomeAuthInvalidStatus(Action? openWebLogin)
    {
        return new VideoListStatus(
            "Home",
            SessionNoLongerValidMessage,
            "dialog-password-symbolic",
            false,
            openWebLogin is null ? null : SignInActionLabel,
            openWebLogin);
    }

    public static VideoListStatus HomeErrorStatus()
    {
        return new VideoListStatus(
            "Home",
            HomeLoadErrorMessage,
            "network-error-symbolic",
            true);
    }

    public static VideoListStatus HomeEmptyStatus()
    {
        return new VideoListStatus(
            "Home",
            HomeEmptyMessage,
            "applications-internet-symbolic");
    }

    /// <summary>
    ///     Maps a feed-level outcome to the Home state kind plus its user-facing
    ///     message: signed-out is decided by callers via <see cref="RequireSignedIn" />
    ///     before reaching here; auth-invalid and generic errors stay distinct so
    ///     the UI can offer Sign In vs Retry respectively; loading/ready states
    ///     keep the engine's own message.
    /// </summary>
    public static (HomeFeedStateKind Kind, string? Message) MapHomeFeedOutcome(
        AuthenticatedHomeFeedStatus lastStatus,
        FeedEngineState state)
    {
        if (IsAuthInvalid(lastStatus))
            return (HomeFeedStateKind.AuthenticationRequired, SessionNoLongerValidMessage);

        if (state.LastError != null || !state.IsSuccess)
            return (HomeFeedStateKind.SafeError, HomeLoadErrorMessage);

        if (state is { IsLoading: true, Videos.Count: 0 })
            return (HomeFeedStateKind.InitialLoading, state.StatusMessage);

        if (state.Videos.Count == 0 && !state.IsLoading)
            return (HomeFeedStateKind.Empty, HomeEmptyMessage);

        return (HomeFeedStateKind.Ready, state.StatusMessage);
    }
}