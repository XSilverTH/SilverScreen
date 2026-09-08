using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Home;
using CoreGate = SilverScreen.Core.Account.Session.SessionGate;

namespace SilverScreen.Features;

/// <summary>
///     App-level session-gate presentation helpers for feed surfaces.
///     Migration complete: every feed, ViewModel, Coordinator, and service now
///     delegates to <see cref="CoreGate" /> in SilverScreen.Core, which is the
///     single source of truth for the signed-in predicate and all gate
///     messages. This class remains as the App-layer facade so existing
///     callers keep compiling; it adds the <see cref="VideoListStatus" />
///     builders and the Home outcome mapping that need App-layer types.
/// </summary>
public enum SessionGateState
{
    SignedIn,
    SignedOut
}

public static class SessionGate
{
    private const string SignInActionLabel = CoreGate.SignInActionLabel;
    private const string HomeSignedOutMessage = CoreGate.HomeSignedOutMessage;
    public const string HistorySignedOutMessage = CoreGate.HistorySignedOutMessage;
    public const string SessionNoLongerValidMessage = CoreGate.SessionNoLongerValidMessage;
    public const string HomeLoadErrorMessage = CoreGate.HomeLoadErrorMessage;
    private const string HomeEmptyMessage = CoreGate.HomeEmptyMessage;

    public static bool RequireSignedIn(ISessionService? sessionService)
    {
        return CoreGate.RequireSignedIn(sessionService);
    }

    public static bool IsAuthInvalid(AuthenticatedHomeFeedStatus status)
    {
        return CoreGate.IsAuthInvalid(status);
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
