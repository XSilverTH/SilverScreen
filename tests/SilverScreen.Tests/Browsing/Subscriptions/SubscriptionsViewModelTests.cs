using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Browsing.Subscriptions;

public sealed class SubscriptionsViewModelTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";

    [Fact]
    public async Task LoadAsync_LoadsChannelsAndFirstFeedPage()
    {
        var channels = new List<SubscribedChannel>
        {
            new("UC1", "Channel 1", "https://www.youtube.com/@chan1", null),
            new("UC2", "Channel 2", "https://www.youtube.com/@chan2", null)
        };
        var videos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v2", "Video 2", "Channel 2", "https://www.youtube.com/@chan2")
        };

        var subsService = new FakeSubscriptionsService(channels, videos);
        var channelService = new FakeChannelService();
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);

        await viewModel.LoadAsync(20);

        Assert.Equal(2, viewModel.State.Channels.Count);
        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.Null(viewModel.State.SelectedChannel);
        Assert.Equal(AuthenticatedSubscriptionsStatus.Success, viewModel.State.Status);
        Assert.True(viewModel.State.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_WhenSignedOut_SetsAuthenticationRequired()
    {
        var subsService = new FakeSubscriptionsService([], []);
        var channelService = new FakeChannelService();
        var session = new InMemorySessionService();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);

        await viewModel.RefreshAsync(20);

        Assert.Equal(AuthenticatedSubscriptionsStatus.AuthenticationRequired, viewModel.State.Status);
        Assert.False(viewModel.State.IsSuccess);
        Assert.Empty(viewModel.State.Videos);
        Assert.Empty(viewModel.State.Channels);
    }

    [Fact]
    public async Task SelectChannelAsync_FiltersInMemoryVideosImmediately()
    {
        var targetChannel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var otherChannel = new SubscribedChannel("UC2", "Channel 2", "https://www.youtube.com/@chan2", null);

        var videos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v2", "Video 2", "Channel 2", "https://www.youtube.com/@chan2"),
            CreateVideo("v3", "Video 3", "Channel 1", "https://www.youtube.com/@chan1")
        };

        var subsService = new FakeSubscriptionsService([targetChannel, otherChannel], videos);
        var channelService = new FakeChannelService();
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        await viewModel.SelectChannelAsync(targetChannel);

        Assert.Equal(targetChannel, viewModel.State.SelectedChannel);
        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.All(viewModel.State.Videos, v => Assert.Equal("Channel 1", v.ChannelName));
    }

    [Fact]
    public async Task SelectChannelAsync_FetchesChannelUploadsInBackgroundAndMerges()
    {
        var targetChannel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var feedVideos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1")
        };
        var channelUploads = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v_extra", "Extra Upload", "Channel 1", "https://www.youtube.com/@chan1")
        };

        var subsService = new FakeSubscriptionsService([targetChannel], feedVideos);
        var channelService = new FakeChannelService(channelUploads, nextStartIndex: 21);
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        await viewModel.SelectChannelAsync(targetChannel);

        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.Contains(viewModel.State.Videos, v => v.Id == "v_extra");
        Assert.True(viewModel.State.HasMore);
    }

    [Fact]
    public async Task SelectChannelAsync_WhenInMemoryMatchesExist_ShowsLoadingMorePillDuringBackgroundFetch()
    {
        var targetChannel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var feedVideos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1")
        };
        var tcs = new TaskCompletionSource<ChannelPage>();
        var channelService = new DelayedFakeChannelService(tcs.Task);
        var subsService = new FakeSubscriptionsService([targetChannel], feedVideos);
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        var selectTask = viewModel.SelectChannelAsync(targetChannel);

        // Immediately, in-memory match is shown and IsLoadingMore is true
        Assert.Single(viewModel.State.Videos);
        Assert.False(viewModel.State.IsLoading);
        Assert.True(viewModel.State.IsLoadingMore);

        // Complete background fetch
        tcs.SetResult(new ChannelPage(
            targetChannel.Url,
            targetChannel.Title,
            null, null, null,
            [
                CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
                CreateVideo("v2", "Video 2", "Channel 1", "https://www.youtube.com/@chan1")
            ],
            ChannelVideoSort.Newest,
            null, true, null));

        await selectTask;

        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.False(viewModel.State.IsLoadingMore);
    }

    [Fact]
    public async Task SelectChannelAsync_WhenAlreadyOnSameChannel_DoesNotReloadOrClearVideos()
    {
        var channel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var feedVideos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1")
        };
        var channelUploads = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v2", "Video 2", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v3", "Video 3", "Channel 1", "https://www.youtube.com/@chan1")
        };

        var subsService = new FakeSubscriptionsService([channel], feedVideos);
        var channelService = new FakeChannelService(channelUploads);
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        // First selection loads and merges uploads (total 3 videos)
        await viewModel.SelectChannelAsync(channel);
        Assert.Equal(3, viewModel.State.Videos.Count);

        // Second selection on the same channel must not clear or reload
        await viewModel.SelectChannelAsync(channel);
        Assert.Equal(3, viewModel.State.Videos.Count);
    }

    [Fact]
    public async Task SelectChannelAsync_ClearingFilterRestoresFeedVideos()
    {
        var channel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var feedVideos = new List<VideoSummary>
        {
            CreateVideo("v1", "Video 1", "Channel 1", "https://www.youtube.com/@chan1"),
            CreateVideo("v2", "Video 2", "Channel 2", "https://www.youtube.com/@chan2")
        };

        var subsService = new FakeSubscriptionsService([channel], feedVideos);
        var channelService = new FakeChannelService();
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        await viewModel.SelectChannelAsync(channel);
        Assert.Single(viewModel.State.Videos);

        await viewModel.SelectChannelAsync(null);
        Assert.Null(viewModel.State.SelectedChannel);
        Assert.Equal(2, viewModel.State.Videos.Count);
    }

    [Fact]
    public async Task LoadMoreAsync_PaginatesFeedWhenNoFilterActive()
    {
        var firstPage = new List<VideoSummary> { CreateVideo("v1", "V1", "C1", "u1") };
        var secondPage = new List<VideoSummary> { CreateVideo("v2", "V2", "C1", "u1") };

        var subsService = new FakeSubscriptionsService([], firstPage, secondPage);
        var channelService = new FakeChannelService();
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);

        Assert.Single(viewModel.State.Videos);

        await viewModel.LoadMoreAsync(20);

        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.Equal("v2", viewModel.State.Videos[1].Id);
    }

    [Fact]
    public async Task LoadMoreAsync_PaginatesChannelWhenFilterActive()
    {
        var channel = new SubscribedChannel("UC1", "Channel 1", "https://www.youtube.com/@chan1", null);
        var initialUploads = new List<VideoSummary> { CreateVideo("v1", "V1", "Channel 1", "https://www.youtube.com/@chan1") };
        var moreUploads = new List<VideoSummary> { CreateVideo("v2", "V2", "Channel 1", "https://www.youtube.com/@chan1") };

        var subsService = new FakeSubscriptionsService([channel], []);
        var channelService = new FakeChannelService(initialUploads, nextStartIndex: 21, secondPage: moreUploads);
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session);
        await viewModel.LoadAsync(20);
        await viewModel.SelectChannelAsync(channel);

        Assert.Single(viewModel.State.Videos);

        await viewModel.LoadMoreAsync(20);

        Assert.Equal(2, viewModel.State.Videos.Count);
        Assert.Equal("v2", viewModel.State.Videos[1].Id);
    }

    [Fact]
    public void IsMatchingChannel_MatchesCorrectly()
    {
        var channel = new SubscribedChannel("UC_123", "Example Channel", "https://www.youtube.com/@example", null);

        var matchingByUrl = CreateVideo("v1", "Title", "Other", "https://www.youtube.com/@example/videos");
        var matchingById = CreateVideo("v2", "Title", "Other", "https://www.youtube.com/channel/UC_123");
        var matchingByTitle = CreateVideo("v3", "Title", "Example Channel", "https://www.youtube.com/different");
        var nonMatching = CreateVideo("v4", "Title", "Different", "https://www.youtube.com/@different");

        Assert.True(SubscriptionsViewModel.IsMatchingChannel(matchingByUrl, channel));
        Assert.True(SubscriptionsViewModel.IsMatchingChannel(matchingById, channel));
        Assert.True(SubscriptionsViewModel.IsMatchingChannel(matchingByTitle, channel));
        Assert.False(SubscriptionsViewModel.IsMatchingChannel(nonMatching, channel));
    }

    [Fact]
    public async Task SessionChanged_ClearsStateWhenLoggedOut()
    {
        var subsService = new FakeSubscriptionsService([], [CreateVideo("v1", "V1", "C1", "u1")]);
        var channelService = new FakeChannelService();
        var session = CreateSession();

        using var viewModel = new SubscriptionsViewModel(subsService, channelService, session, subscribeSessionEvents: true);
        await viewModel.LoadAsync(20);

        Assert.Single(viewModel.State.Videos);

        session.ClearSession();

        Assert.Equal(AuthenticatedSubscriptionsStatus.AuthenticationRequired, viewModel.State.Status);
        Assert.Empty(viewModel.State.Videos);
    }

    private static InMemorySessionService CreateSession()
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        return session;
    }

    private static VideoSummary CreateVideo(string id, string title, string channel, string channelUrl)
    {
        return new VideoSummary(
            id,
            title,
            channel,
            TimeSpan.FromMinutes(5),
            "https://img.example/thumb.jpg",
            false,
            $"https://www.youtube.com/watch?v={id}",
            null,
            DateTimeOffset.UtcNow,
            channelUrl);
    }

    private sealed class FakeSubscriptionsService(
        IReadOnlyList<SubscribedChannel> channels,
        IReadOnlyList<VideoSummary> firstFeedPage,
        IReadOnlyList<VideoSummary>? secondFeedPage = null) : IAuthenticatedSubscriptionsService
    {
        private int _feedPageCalls;

        public Task<AuthenticatedSubscriptionsFeedResult> LoadFirstFeedPageAsync(
            int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            _feedPageCalls = 1;
            var continuation = secondFeedPage is { Count: > 0 } ? "21" : null;
            return Task.FromResult(new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Success,
                new FeedPage(firstFeedPage, continuation),
                "Success"));
        }

        public Task<AuthenticatedSubscriptionsFeedResult> LoadNextFeedPageAsync(
            int count = VideoFeedConstants.DefaultPageSize,
            CancellationToken cancellationToken = default)
        {
            _feedPageCalls++;
            var videos = secondFeedPage ?? [];
            return Task.FromResult(new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Success,
                new FeedPage(videos, null),
                "Success"));
        }

        public Task<SubscribedChannelsResult> LoadSubscribedChannelsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.Success,
                channels,
                "Success"));
        }
    }

    private sealed class FakeChannelService(
        IReadOnlyList<VideoSummary>? uploads = null,
        int? nextStartIndex = null,
        IReadOnlyList<VideoSummary>? secondPage = null) : IChannelService
    {
        private int _calls;

        public Task<ChannelPage> GetChannelAsync(
            string channelUrl,
            string fallbackName,
            ChannelVideoSort sort,
            int startIndex,
            int count,
            CancellationToken cancellationToken)
        {
            _calls++;
            var list = _calls > 1 && secondPage != null ? secondPage : (uploads ?? []);
            var next = _calls > 1 ? null : nextStartIndex;
            return Task.FromResult(new ChannelPage(
                channelUrl,
                fallbackName,
                "Description",
                "https://img.example/avatar.jpg",
                1000,
                list,
                sort,
                null,
                true,
                next));
        }
    }

    private sealed class DelayedFakeChannelService(Task<ChannelPage> task) : IChannelService
    {
        public Task<ChannelPage> GetChannelAsync(
            string channelUrl,
            string fallbackName,
            ChannelVideoSort sort,
            int startIndex,
            int count,
            CancellationToken cancellationToken)
        {
            return task;
        }
    }
}
