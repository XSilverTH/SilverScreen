namespace SilverScreen.Core.Models;

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
    IReadOnlyList<VideoSummary> Videos,
    string? Message = null,
    bool IsLoading = false,
    bool IsLoadingMore = false,
    bool HasContinuation = false)
{
    public static HomeFeedState SignedOut { get; } = new(HomeFeedStateKind.SignedOut, []);
    public static HomeFeedState InitialLoading { get; } = new(HomeFeedStateKind.InitialLoading, [], IsLoading: true);
}