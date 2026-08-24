using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Browsing.Components;

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
