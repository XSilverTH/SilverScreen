using System.Net;
using Adw;
using SilverScreen.Browsing.Channel;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Domain;
using SilverScreen.Shell.Notifications;

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
    public async Task OpenChannelAsync_WithPlaceholderId_DoesNotLoadChannel()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);

        await viewModel.OpenChannelAsync("https://www.youtube.com/channel/UC0000000000000000000000", "Fallback");

        Assert.Null(service.LastRequest);
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
    public async Task ChannelSortChange_FailedFetchDoesNotRetainPreviousResultsOrContinuation()
    {
        var service = new IdentityChangingChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        Assert.Single(viewModel.State.Videos);
        Assert.True(viewModel.State.HasMore);

        await viewModel.SetSortSelection(2);

        Assert.Empty(viewModel.State.Videos);
        Assert.False(viewModel.State.HasMore);
        Assert.False(viewModel.State.IsSuccess);
        Assert.Equal("Could not load channel.", viewModel.State.Summary);
    }


    [Fact]
    public async Task ChannelChange_FailedFetchDoesNotRetainPreviousResultsOrContinuation()
    {
        var service = new IdentityChangingChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        await viewModel.OpenChannelAsync("https://www.youtube.com/@other", "Other");

        Assert.Empty(viewModel.State.Videos);
        Assert.False(viewModel.State.HasMore);
        Assert.False(viewModel.State.IsSuccess);
        Assert.Equal("Could not load channel.", viewModel.State.Summary);
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
    public async Task NavigationState_RestoresLoadedPagesAndSortWithoutFetchingAgain()
    {
        var service = new FakeChannelService();
        using var viewModel = new ChannelViewModel(service);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");
        await viewModel.LoadMoreAsync();
        await viewModel.SetSortSelection(2);
        await viewModel.LoadMoreAsync();
        var saved = viewModel.CaptureNavigationState();
        var requestsBeforeRestore = service.RequestCount;

        await viewModel.OpenChannelAsync("https://www.youtube.com/@other", "Other");
        viewModel.RestoreNavigationState(saved);

        Assert.Equal(requestsBeforeRestore + 1, service.RequestCount);
        Assert.Equal("https://www.youtube.com/@example", viewModel.State.Url);
        Assert.Equal(ChannelVideoSort.Popular, viewModel.State.Sort);
        Assert.Equal(["dQw4w9WgXcQ", "abc123def45"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }


    [Fact]
    public async Task LoadMoreAsync_DoesNotOverwriteExistingMetadataWithNullOrEmpty()
    {
        var service = new FakeMetadataPaginatingChannelService();
        using var viewModel = new ChannelViewModel(service);
        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");

        Assert.Equal("Original Channel", viewModel.State.Name);
        Assert.Equal("Original Description", viewModel.State.Description);
        Assert.Equal("https://example.com/avatar.jpg", viewModel.State.AvatarUrl);
        Assert.Equal(42000L, viewModel.State.SubscriberCount);
        await viewModel.LoadMoreAsync();

        Assert.Equal("Original Channel", viewModel.State.Name);
        Assert.Equal("Original Description", viewModel.State.Description);
        Assert.Equal("https://example.com/avatar.jpg", viewModel.State.AvatarUrl);
        Assert.Equal(42000L, viewModel.State.SubscriberCount);
    }

    [Fact]
    public async Task ToggleSubscriptionAsync_SubscribesAndUnsubscribesUsingChannelIdAndShowsToast()
    {
        var service = new FakeChannelService
        {
            ChannelIdToReturn = "UC1234567890123456789012",
            InitialIsSubscribed = false
        };
        var session = new FakeSessionService(isSignedIn: true);
        var notifications = new FakeNotificationService();
        using var viewModel = new ChannelViewModel(service, session, notifications);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");
        Assert.False(viewModel.State.IsSubscribed);
        Assert.Equal("UC1234567890123456789012", viewModel.State.ChannelId);

        await viewModel.ToggleSubscriptionAsync();

        Assert.True(viewModel.State.IsSubscribed);
        Assert.False(viewModel.State.IsSubscriptionBusy);
        Assert.Equal(("UC1234567890123456789012", true), service.LastSubscriptionCall);
        Assert.Equal("Subscribed to “Example Channel”", notifications.LastMessage);

        await viewModel.ToggleSubscriptionAsync();

        Assert.False(viewModel.State.IsSubscribed);
        Assert.False(viewModel.State.IsSubscriptionBusy);
        Assert.Equal(("UC1234567890123456789012", false), service.LastSubscriptionCall);
        Assert.Equal("Unsubscribed from “Example Channel”", notifications.LastMessage);
    }

    [Fact]
    public async Task ToggleSubscriptionAsync_WhenSignedOut_ShowsSignInToastAndDoesNotCallService()
    {
        var service = new FakeChannelService();
        var session = new FakeSessionService(isSignedIn: false);
        var notifications = new FakeNotificationService();
        using var viewModel = new ChannelViewModel(service, session, notifications);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");
        await viewModel.ToggleSubscriptionAsync();

        Assert.Null(service.LastSubscriptionCall);
        Assert.False(viewModel.State.IsSubscribed);
        Assert.Equal("Sign in to subscribe to channels.", notifications.LastMessage);
    }

    [Fact]
    public async Task ToggleSubscriptionAsync_WhenServiceFails_KeepsPreviousStateAndShowsErrorToast()
    {
        var service = new FakeChannelService
        {
            InitialIsSubscribed = false,
            FailNextSubscription = true
        };
        var session = new FakeSessionService(isSignedIn: true);
        var notifications = new FakeNotificationService();
        using var viewModel = new ChannelViewModel(service, session, notifications);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");
        await viewModel.ToggleSubscriptionAsync();

        Assert.False(viewModel.State.IsSubscribed);
        Assert.False(viewModel.State.IsSubscriptionBusy);
        Assert.Equal("Could not subscribe to channel.", notifications.LastMessage);
    }

    [Fact]
    public async Task NavigationState_RestoresIsSubscribedAndChannelId()
    {
        var service = new FakeChannelService
        {
            ChannelIdToReturn = "UC1234567890123456789012",
            InitialIsSubscribed = true
        };
        using var viewModel = new ChannelViewModel(service);

        await viewModel.OpenChannelAsync("https://www.youtube.com/@example", "Example");
        var saved = viewModel.CaptureNavigationState();

        await viewModel.OpenChannelAsync("https://www.youtube.com/@other", "Other");
        viewModel.RestoreNavigationState(saved);

        Assert.True(viewModel.State.IsSubscribed);
        Assert.Equal("UC1234567890123456789012", viewModel.State.ChannelId);
    }

    private sealed class IdentityChangingChannelService : IChannelService
    {
        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            string? continuationToken, int count, CancellationToken cancellationToken)
        {
            if (sort == ChannelVideoSort.Popular || channelUrl.Contains("@other", StringComparison.Ordinal))
            {
                var message = sort == ChannelVideoSort.Popular ? "Popular failed" : "Other failed";
                return Task.FromResult(ChannelPage.Failed(channelUrl, fallbackName, sort, message));
            }

            var video = new VideoSummary("old-channel-video", "Old", fallbackName, TimeSpan.FromMinutes(1), "", false,
                ChannelUrl: channelUrl);
            return Task.FromResult(new ChannelPage(channelUrl, fallbackName, null, null, null, [video], sort,
                "Loaded", true, "old-next"));
        }
    }

    private sealed class FakeMetadataPaginatingChannelService : IChannelService
    {
        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            string? continuationToken, int count, CancellationToken cancellationToken)
        {
            if (continuationToken is null)
                return Task.FromResult(new ChannelPage(
                    channelUrl,
                    "Original Channel",
                    "Original Description",
                    "https://example.com/avatar.jpg",
                    42000L,
                    [
                        new VideoSummary("v1", "V1", "Original Channel", TimeSpan.FromMinutes(1), "", false,
                            ChannelUrl: channelUrl)
                    ],
                    sort,
                    "Loaded",
                    NextContinuationToken: "page2"));

            return Task.FromResult(new ChannelPage(
                channelUrl,
                fallbackName,
                null,
                null,
                null,
                [
                    new VideoSummary("v2", "V2", fallbackName, TimeSpan.FromMinutes(1), "", false,
                        ChannelUrl: channelUrl)
                ],
                sort,
                "Loaded page 2",
                NextContinuationToken: null));
        }
    }


    private sealed class FakeChannelService : IChannelService
    {
        public (string Url, string FallbackName, ChannelVideoSort Sort, string? ContinuationToken, int Count)?
            LastRequest { get; private set; }

        public (string Target, bool Subscribe)? LastSubscriptionCall { get; private set; }

        public int RequestCount { get; private set; }
        public string? ChannelIdToReturn { get; init; }
        public bool? InitialIsSubscribed { get; init; }
        public bool FailNextSubscription { get; set; }

        public Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
            string? continuationToken, int count, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = (channelUrl, fallbackName, sort, continuationToken, count);
            var video = continuationToken is null
                ? new VideoSummary("dQw4w9WgXcQ", "Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl)
                : new VideoSummary("abc123def45", "More Video", "Example Channel", TimeSpan.FromSeconds(42),
                    string.Empty, false, ChannelUrl: channelUrl);
            return Task.FromResult(new ChannelPage(channelUrl, "Example Channel", "Description", null, 1,
                [video], sort, "Loaded Example Channel.",
                NextContinuationToken: continuationToken is null ? "next" : null,
                ChannelId: ChannelIdToReturn,
                IsSubscribed: InitialIsSubscribed));
        }

        public Task<ChannelSubscriptionResult> SubscribeAsync(string channelIdOrUrl,
            CancellationToken cancellationToken = default)
        {
            LastSubscriptionCall = (channelIdOrUrl, true);
            if (FailNextSubscription)
                return Task.FromResult(new ChannelSubscriptionResult(false, false, "Could not subscribe to channel."));

            return Task.FromResult(new ChannelSubscriptionResult(true, true, null, ChannelIdToReturn));
        }

        public Task<ChannelSubscriptionResult> UnsubscribeAsync(string channelIdOrUrl,
            CancellationToken cancellationToken = default)
        {
            LastSubscriptionCall = (channelIdOrUrl, false);
            if (FailNextSubscription)
                return Task.FromResult(new ChannelSubscriptionResult(false, true, "Could not unsubscribe from channel."));

            return Task.FromResult(new ChannelSubscriptionResult(true, false, null, ChannelIdToReturn));
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public string? LastMessage { get; private set; }

        public void ShowToast(string message) => LastMessage = message;

        public void ShowToast(Toast toast)
        {
        }
    }

    private sealed class FakeSessionService(bool isSignedIn) : ISessionService
    {
        public event EventHandler? SessionChanged
        {
            add { }
            remove { }
        }

        public AccountSession GetCurrentSession()
        {
            return isSignedIn
                ? new AccountSession(true, "User", HasManualSession: true,
                    CookieFormat: SessionCookieFormat.NetscapeCookiesText)
                : AccountSession.SignedOut;
        }

        public ManualSessionCookies? GetManualSessionCookies()
        {
            return isSignedIn
                ? new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, "cookies")
                : null;
        }

        public void SetManualSession(string cookieContent, SessionCookieFormat format)
        {
        }

        public void ClearSession()
        {
        }

        public CookieFileLease? AcquireCookieFileLease() => null;

        public CookieContainer? CreateCookieContainer() => null;
    }
}