using SilverScreen.Browsing.History;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;

namespace SilverScreen.Tests.Browsing.History;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task LoadMoreAsync_AppendsTheNextServerPageWithoutDuplicatingVideos()
    {
        var service = new FakeHistoryService
        {
            FirstPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v1")], "next"),
                "Watch history loaded."),
            NextPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v1"), CreateVideo("v2")]),
                "Watch history loaded.")
        };
        using var viewModel = new HistoryViewModel(service);
        await viewModel.LoadAsync();

        await viewModel.LoadMoreAsync();

        Assert.Equal(1, service.NextPageCallCount);
        Assert.Equal(["v1", "v2"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }
    [Fact]
    public async Task RefreshAndLoadMoreAsync_WithCustomCount_PropagatesCountToService()
    {
        var service = new FakeHistoryService
        {
            FirstPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v1")], "next"),
                "Watch history loaded."),
            NextPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v2")]),
                "Watch history loaded.")
        };
        using var viewModel = new HistoryViewModel(service);
        await viewModel.RefreshAsync(count: 40);
        Assert.Equal(40, Assert.Single(service.FirstPageCounts));

        await viewModel.LoadMoreAsync(count: 40);
        Assert.Equal(40, Assert.Single(service.NextPageCounts));
    }



    private static VideoSummary CreateVideo(string id)
    {
        return new VideoSummary(id, $"Video {id}", "Channel", TimeSpan.FromMinutes(3), "thumbnail", false);
    }

    private sealed class FakeHistoryService : IAuthenticatedHistoryService
    {
        public AuthenticatedHistoryResult FirstPage { get; init; } = new(
            AuthenticatedHistoryStatus.Empty, FeedPage.Empty, "No watch history was returned.");

        public AuthenticatedHistoryResult NextPage { get; init; } = new(
            AuthenticatedHistoryStatus.Empty, FeedPage.Empty, "No additional watch history is available.");

        public int NextPageCallCount { get; private set; }
        public List<int> FirstPageCounts { get; } = [];
        public List<int> NextPageCounts { get; } = [];

        public Task<AuthenticatedHistoryResult> LoadFirstPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            FirstPageCounts.Add(count);
            return Task.FromResult(FirstPage);
        }

        public Task<AuthenticatedHistoryResult> LoadNextPageAsync(int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            NextPageCallCount++;
            NextPageCounts.Add(count);
            return Task.FromResult(NextPage);
        }
    }
}