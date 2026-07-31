using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class ChannelViewModelTests
{
    [Fact]
    public async Task OpenChannelAsync_LoadsTheRequestedChannelAndDefaultsToNewest()
    {
        var service = new FakeChannelService();
        var reporter = new FakeStatusReporter();
        using var viewModel = new ChannelViewModel(service, reporter);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        Assert.Equal(("https://www.youtube.com/@example", "Example", ChannelVideoSort.Newest, 1), service.LastRequest);
        Assert.Equal("Example Channel", viewModel.State.Name);
        Assert.Single(viewModel.State.Videos);
        Assert.False(viewModel.State.IsLoading);
        Assert.True(viewModel.State.IsSuccess);
        Assert.Equal("Loaded Example Channel.", reporter.LastStatus);
    }

    [Fact]
    public async Task SetSortSelection_ReloadsCurrentChannelWithChosenSort()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service, new FakeStatusReporter());
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
        using var viewModel = new ChannelViewModel(service, new FakeStatusReporter());
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        await viewModel.LoadMoreAsync();

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Newest, 21),
            service.LastRequest);
        Assert.Equal(["dQw4w9WgXcQ", "abc123def45"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }

    private sealed class FakeChannelService : IChannelService
    {
        public (string Url, string FallbackName, ChannelVideoSort Sort, int StartIndex)? LastRequest { get; private set; }

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

    private sealed class FakeStatusReporter : IStatusReporter
    {
        public string? LastStatus { get; private set; }

        public void ReportStatus(string status)
        {
            LastStatus = status;
        }
    }
}
