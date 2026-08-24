using SilverScreen.Core.Browsing.Common;
namespace SilverScreen.Core.Browsing.Home;

public enum HomeFeedStateKind
{
    SignedOut,
    InitialLoading,
    Ready,
    Empty,
    AuthenticationRequired,
    SafeError
}

public sealed record HomeFeedState(
    HomeFeedStateKind Kind,
    VideoSummary[] Videos,
    string? Message = null,
    bool IsLoading = false,
    bool IsLoadingMore = false,
    bool HasContinuation = false)
{
    public static HomeFeedState SignedOut { get; } = new(HomeFeedStateKind.SignedOut, []);
}