using SilverScreen.Core.Browsing;
using SilverScreen.Core.Domain;

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
    string PaginationLoadingMessage = "Loading more videos…",
    string? PaginationError = null);

public interface IVideoListSource : IDisposable
{
    bool HasMore { get; }
    VideoListPresentationState State { get; }
    event EventHandler<VideoListPresentationState>? StateChanged;
    Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize);
    Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize);
}