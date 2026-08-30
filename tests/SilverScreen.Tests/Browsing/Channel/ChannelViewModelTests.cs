using SilverScreen.Browsing.Channel;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Tests.Browsing.Channel;

public sealed class ChannelViewModelTests
{
    [Fact]
    public async Task OpenChannelAsync_LoadsTheRequestedChannelAndDefaultsToNewest()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        Assert.Equal(("https://www.youtube.com/@example", "Example", ChannelVideoSort.Newest, null,
            VideoFeedConstants.DefaultPageSize), service.LastRequest);
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

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Popular, null,
            VideoFeedConstants.DefaultPageSize), service.LastRequest);
        Assert.Equal(ChannelVideoSort.Popular, viewModel.State.Sort);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsTheNextChannelPage()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        await viewModel.LoadMoreAsync();

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Newest, "next",
            VideoFeedConstants.DefaultPageSize), service.LastRequest);
        Assert.Equal(["dQw4w9WgXcQ", "abc123def45"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }
    [Fact]
    public async Task OpenChannelAndLoadMore_WithCustomCount_PropagatesCountToChannelService()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example", count: 40);

        Assert.Equal(("https://www.youtube.com/@example", "Example", ChannelVideoSort.Newest, null, 40),
            service.LastRequest);

        await viewModel.LoadMoreAsync(count: 40);

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Newest, "next", 40),
            service.LastRequest);
    }


    private sealed class FakeChannelService : IChannelService
    {
        public (string Url, string FallbackName, ChannelVideoSort Sort, string? ContinuationToken, int Count)? LastRequest
        {
            get;
            private set;
        }

        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            string? continuationToken, int count, CancellationToken cancellationToken)
        {
            LastRequest = (channelUrl, fallbackName, sort, continuationToken, count);
            var video = continuationToken is null
                ? new VideoSummary("dQw4w9WgXcQ", "Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl)
                : new VideoSummary("abc123def45", "More Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl);
            return Task.FromResult(new ChannelPage(channelUrl, "Example Channel", "Description", null, 1,
                [video], sort, "Loaded Example Channel.", NextContinuationToken: continuationToken is null ? "next" : null));
        }
    }
}