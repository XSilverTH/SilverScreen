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


namespace SilverScreen.Tests.Browsing.Channel;

public sealed class ChannelViewModelTests
{
    [Fact]
    public async Task OpenChannelAsync_LoadsTheRequestedChannelAndDefaultsToNewest()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        Assert.Equal(("https://www.youtube.com/@example", "Example", ChannelVideoSort.Newest, 1), service.LastRequest);
        Assert.Equal("Example Channel", viewModel.State.Name);
        Assert.Single(viewModel.State.Videos);
        Assert.False(viewModel.State.IsLoading);
        Assert.True(viewModel.State.IsSuccess);
    }

    [Fact]
    public async Task SetSortSelection_ReloadsCurrentChannelWithChosenSort()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        await viewModel.SetSortSelection(2);

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Popular, 1),
            service.LastRequest);
        Assert.Equal(ChannelVideoSort.Popular, viewModel.State.Sort);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsTheNextChannelPage()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        await viewModel.LoadMoreAsync();

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Newest, 21),
            service.LastRequest);
        Assert.Equal(["dQw4w9WgXcQ", "abc123def45"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }

    private sealed class FakeChannelService : IChannelService
    {
        public (string Url, string FallbackName, ChannelVideoSort Sort, int StartIndex)? LastRequest
        {
            get;
            private set;
        }

        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            int startIndex, CancellationToken cancellationToken)
        {
            LastRequest = (channelUrl, fallbackName, sort, startIndex);
            var video = startIndex == 1
                ? new VideoSummary("dQw4w9WgXcQ", "Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl)
                : new VideoSummary("abc123def45", "More Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl);
            return Task.FromResult(new ChannelPage(channelUrl, "Example Channel", "Description", null, 1,
                [video], sort, "Loaded Example Channel.", NextStartIndex: startIndex == 1 ? 21 : null));
        }
    }
}
