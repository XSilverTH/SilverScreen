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

        Assert.Equal(("https://www.youtube.com/@example", "Example", ChannelVideoSort.Newest), service.LastRequest);
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

        Assert.Equal(("https://www.youtube.com/@example", "Example Channel", ChannelVideoSort.Popular),
            service.LastRequest);
        Assert.Equal(ChannelVideoSort.Popular, viewModel.State.Sort);
    }

    private sealed class FakeChannelService : IChannelService
    {
        public (string Url, string FallbackName, ChannelVideoSort Sort)? LastRequest { get; private set; }

        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            CancellationToken cancellationToken)
        {
            LastRequest = (channelUrl, fallbackName, sort);
            var video = new VideoSummary("dQw4w9WgXcQ", "Video", "Example Channel", TimeSpan.FromSeconds(42),
                string.Empty, false, ChannelUrl: channelUrl);
            return Task.FromResult(new ChannelPage(channelUrl, "Example Channel", "Description", null, 1,
                [video], sort, "Loaded Example Channel."));
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
