using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Browsing.Components;

public sealed record VideoListStatus(
    string Title,
    string? Description,
    string IconName,
    bool ShowRetry = false,
    string? ActionLabel = null,
    Action? Action = null);

public sealed record VideoListPresentationState(
    IReadOnlyList<VideoSummary> Videos,
    bool IsLoading,
    bool IsLoadingMore,
    VideoListStatus Status,
    string? LoadingMessage = null,
    string PaginationLoadingMessage = "Loading more videos…");

public interface IVideoListSource : IDisposable
{
    VideoListPresentationState State { get; }
    event EventHandler<VideoListPresentationState>? StateChanged;
    Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize);
    Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize);
}