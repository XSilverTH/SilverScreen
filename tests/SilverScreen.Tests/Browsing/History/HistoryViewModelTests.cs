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
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


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

        public Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FirstPage);
        }

        public Task<AuthenticatedHistoryResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
        {
            NextPageCallCount++;
            return Task.FromResult(NextPage);
        }
    }
}
