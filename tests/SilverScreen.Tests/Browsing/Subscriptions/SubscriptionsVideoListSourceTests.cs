using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Subscriptions;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;

namespace SilverScreen.Tests.Browsing.Subscriptions;

public sealed class SubscriptionsVideoListSourceTests
{
    [Fact]
    public void MapState_AuthenticationRequired_SetsSignInAction()
    {
        var signInCalled = false;
        var state = new SubscriptionsViewState(
            [],
            null,
            [],
            false,
            false,
            false,
            AuthenticatedSubscriptionsStatus.AuthenticationRequired,
            "Sign in to see subscriptions.",
            false);

        var mapped = SubscriptionsVideoListSource.MapState(state, () => signInCalled = true);

        Assert.Equal("Sign in to see subscriptions", mapped.Status.Title);
        Assert.Equal("Sign In", mapped.Status.ActionLabel);
        Assert.NotNull(mapped.Status.Action);
        mapped.Status.Action?.Invoke();
        Assert.True(signInCalled);
    }

    [Fact]
    public void MapState_TemporaryBackendFailure_SetsRetryButton()
    {
        var state = new SubscriptionsViewState(
            [],
            null,
            [],
            false,
            false,
            false,
            AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
            "Network error occurred.",
            false);

        var mapped = SubscriptionsVideoListSource.MapState(state);

        Assert.Equal("Could not load subscriptions", mapped.Status.Title);
        Assert.Equal("Network error occurred.", mapped.Status.Description);
        Assert.True(mapped.Status.ShowRetry);
    }

    [Fact]
    public void MapState_EmptySubscriptions_ShowsNoSubscriptionsMessage()
    {
        var state = new SubscriptionsViewState(
            [],
            null,
            [],
            false,
            false,
            false,
            AuthenticatedSubscriptionsStatus.Success,
            string.Empty,
            true);

        var mapped = SubscriptionsVideoListSource.MapState(state);

        Assert.Equal("No subscriptions", mapped.Status.Title);
        Assert.Equal("Channels you subscribe to on YouTube will appear here.", mapped.Status.Description);
        Assert.False(mapped.Status.ShowRetry);
    }

    [Fact]
    public void MapState_FilteredEmptyState_ShowsChannelSpecificMessage()
    {
        var channel = new SubscribedChannel("UC1", "My Favorite Channel", "https://youtube.com/@fav", null);
        var state = new SubscriptionsViewState(
            [channel],
            channel,
            [],
            false,
            false,
            false,
            AuthenticatedSubscriptionsStatus.Success,
            string.Empty,
            true);

        var mapped = SubscriptionsVideoListSource.MapState(state);

        Assert.Equal("No videos found", mapped.Status.Title);
        Assert.Equal("No videos found for My Favorite Channel.", mapped.Status.Description);
    }

    [Fact]
    public void MapState_SuccessWithVideos_ReturnsVideos()
    {
        var videos = new List<VideoSummary>
        {
            new("v1", "Title 1", "Channel", TimeSpan.FromMinutes(3), "thumb", false, "url", null, null, null)
        };
        var state = new SubscriptionsViewState(
            [],
            null,
            videos,
            false,
            false,
            true,
            AuthenticatedSubscriptionsStatus.Success,
            string.Empty,
            true);

        var mapped = SubscriptionsVideoListSource.MapState(state);

        Assert.Single(mapped.Videos);
        Assert.Equal("v1", mapped.Videos[0].Id);
        Assert.Equal("Subscriptions", mapped.Status.Title);
    }
}
