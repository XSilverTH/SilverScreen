using SilverScreen.Core.Models;

namespace SilverScreen.Views.VideoList;

public sealed record VideoListStatus(
    string Title,
    string? Description,
    string IconName,
    bool ShowRetry = false);

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
    Task RefreshAsync();
    Task LoadMoreAsync();
}
