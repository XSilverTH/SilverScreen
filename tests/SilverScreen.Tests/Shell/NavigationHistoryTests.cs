using SilverScreen.Browsing.Channel;
using SilverScreen.Shell;
using Xunit;

namespace SilverScreen.Tests.Shell;

public sealed class NavigationHistoryTests
{
    [Fact]
    public void InitialState_DefaultsToHomeWithoutHistory()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        Assert.Equal(NavigationPage.Home, nav.CurrentPage);
        Assert.Null(nav.CurrentParameter);
        Assert.False(nav.CanGoBack);
        Assert.Null(nav.PreviousPage);
        Assert.Null(nav.PreviousParameter);
    }

    [Fact]
    public void NavigateTo_SamePageWithoutParameter_ReturnsFalseAndDoesNotPush()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        var result = nav.NavigateTo(NavigationPage.Home);

        Assert.False(result);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void NavigateTo_DetailPages_PushesToBackStackAndGoesBackInReverseOrder()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        Assert.True(nav.NavigateTo(NavigationPage.Search, "query"));
        var channel = new ChannelNavigationArgs("https://youtube.com/@channel", "Channel");
        Assert.True(nav.NavigateTo(NavigationPage.Channel, channel));

        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(NavigationPage.Search, nav.PreviousPage);
        Assert.True(nav.CanGoBack);

        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Search, nav.CurrentPage);
        Assert.Equal(NavigationPage.Home, nav.PreviousPage);

        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Home, nav.CurrentPage);
        Assert.Null(nav.PreviousPage);
        Assert.False(nav.CanGoBack);

        Assert.False(nav.GoBack());
    }

    [Fact]
    public void NavigateTo_NestedChannels_PushesEachChannelToBackStack()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        var channelA = new ChannelNavigationArgs("https://youtube.com/@channelA", "Channel A");
        var channelB = new ChannelNavigationArgs("https://youtube.com/@channelB", "Channel B");
        var channelC = new ChannelNavigationArgs("https://youtube.com/@channelC", "Channel C");

        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelA));
        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(channelA, nav.CurrentParameter);
        Assert.Equal(NavigationPage.Home, nav.PreviousPage);

        // Nested channel navigation: Channel A -> Channel B
        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelB));
        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(channelB, nav.CurrentParameter);
        Assert.Equal(NavigationPage.Channel, nav.PreviousPage);
        Assert.Equal(channelA, nav.PreviousParameter);

        // Nested channel navigation: Channel B -> Channel C
        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelC));
        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(channelC, nav.CurrentParameter);

        // Pop back to Channel B
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(channelB, nav.CurrentParameter);

        // Pop back to Channel A
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Channel, nav.CurrentPage);
        Assert.Equal(channelA, nav.CurrentParameter);

        // Pop back to Home
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Home, nav.CurrentPage);
        Assert.Null(nav.CurrentParameter);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void NavigateTo_SamePageWithSameParameter_ReturnsFalse()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        var channelA = new ChannelNavigationArgs("https://youtube.com/@channelA", "Channel A");

        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelA));
        Assert.False(nav.NavigateTo(NavigationPage.Channel, new ChannelNavigationArgs("https://youtube.com/@channelA", "Channel A")));

        // Back stack should only have Home, not a duplicate Channel A
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Home, nav.CurrentPage);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void PageChanged_FiresWithCorrectPayloadAndDirection()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        NavigationPageChangedEventArgs? lastArgs = null;
        nav.PageChanged += (_, args) => lastArgs = args;

        var channelArgs = new ChannelNavigationArgs("https://youtube.com/@channelA", "Channel A");
        nav.NavigateTo(NavigationPage.Channel, channelArgs);

        Assert.NotNull(lastArgs);
        Assert.Equal(NavigationPage.Home, lastArgs.PreviousPage);
        Assert.Equal(NavigationPage.Channel, lastArgs.CurrentPage);
        Assert.Null(lastArgs.PreviousParameter);
        Assert.Equal(channelArgs, lastArgs.CurrentParameter);
        Assert.False(lastArgs.IsBackNavigation);

        nav.GoBack();

        Assert.NotNull(lastArgs);
        Assert.Equal(NavigationPage.Channel, lastArgs.PreviousPage);
        Assert.Equal(NavigationPage.Home, lastArgs.CurrentPage);
        Assert.Equal(channelArgs, lastArgs.PreviousParameter);
        Assert.Null(lastArgs.CurrentParameter);
        Assert.True(lastArgs.IsBackNavigation);
    }

    [Fact]
    public void SyncFromViewStack_SelectsNewRootWithoutCreatingBackEntry()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        nav.SyncFromViewStack("subscriptions");

        Assert.Equal(NavigationPage.Subscriptions, nav.CurrentPage);
        Assert.Null(nav.PreviousPage);
        Assert.False(nav.CanGoBack);

        nav.NavigateTo(NavigationPage.Search, "query");
        Assert.Equal(NavigationPage.Subscriptions, nav.PreviousPage);

        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Subscriptions, nav.CurrentPage);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void NavigatingFromPlayerToChannel_DoesNotAddPlayerToBackStack()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Subscriptions);

        Assert.True(nav.NavigateTo(NavigationPage.Player));
        var channel = new ChannelNavigationArgs("https://youtube.com/@channel", "Channel");
        Assert.True(nav.NavigateTo(NavigationPage.Channel, channel));

        Assert.Equal(NavigationPage.Subscriptions, nav.PreviousPage);
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Subscriptions, nav.CurrentPage);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void NavigatingBetweenChannelsThroughPlayer_ReturnsToPageBeforeVideo()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);
        var channelA = new ChannelNavigationArgs("https://youtube.com/@channelA", "Channel A");
        var channelB = new ChannelNavigationArgs("https://youtube.com/@channelB", "Channel B");

        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelA));
        Assert.True(nav.NavigateTo(NavigationPage.Player));
        Assert.True(nav.NavigateTo(NavigationPage.Channel, channelB));

        Assert.Equal(NavigationPage.Channel, nav.PreviousPage);
        Assert.Equal(channelA, nav.PreviousParameter);
        Assert.True(nav.GoBack());
        Assert.Equal(channelA, nav.CurrentParameter);
    }

    [Fact]
    public void DirectVideoPlayback_FromHome_PushesOnlyHomeBeforePlayer()
    {
        using var nav = new NavigationService();
        nav.Initialize(NavigationPage.Home);

        // Direct playback navigates straight to Player without Search intermediate page
        Assert.True(nav.NavigateTo(NavigationPage.Player));
        Assert.Equal(NavigationPage.Player, nav.CurrentPage);
        Assert.Equal(NavigationPage.Home, nav.PreviousPage);

        // Closing player returns directly to Home
        Assert.True(nav.GoBack());
        Assert.Equal(NavigationPage.Home, nav.CurrentPage);
        Assert.False(nav.CanGoBack);
    }
}
