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


namespace SilverScreen.Tests.Browsing.Components;

public sealed class VideoListSourceTests
{
    [Fact]
    public void HomeVideoListSource_MapsSignedOutState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.SignedOut, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.Equal("Sign in to see your YouTube recommendations.", presentation.Status.Description);
        Assert.Equal("avatar-default-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
        Assert.False(presentation.IsLoading);
        Assert.Equal("Loading more videos…", presentation.PaginationLoadingMessage);
    }

    [Fact]
    public void HomeVideoListSource_MapsInitialLoadingState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.InitialLoading, [], IsLoading: true);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.Null(presentation.LoadingMessage);
    }

    [Fact]
    public void HomeVideoListSource_MapsReadyAndEmptyState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.Ready, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.Equal("No recommendations are available right now.", presentation.Status.Description);
        Assert.Equal("applications-internet-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
        Assert.False(presentation.IsLoading);
    }

    [Fact]
    public void HomeVideoListSource_MapsAuthenticationRequiredState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.AuthenticationRequired, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.Equal("Your YouTube session is no longer valid.", presentation.Status.Description);
        Assert.Equal("dialog-password-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void HomeVideoListSource_MapsSafeErrorState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.SafeError, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.Equal("Could not load YouTube recommendations.", presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void SearchVideoListSource_MapsSuccessEmptyState()
    {
        var state = new SearchViewState([], "Search complete.", false, false, false, true);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.Equal("No results found", presentation.Status.Title);
        Assert.Equal("Try different keywords or check spelling.", presentation.Status.Description);
        Assert.Equal("system-search-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
        Assert.False(presentation.IsLoading);
        Assert.Equal("Loading more results…", presentation.PaginationLoadingMessage);
    }

    [Fact]
    public void SearchVideoListSource_MapsCustomEmptySummary()
    {
        var state = new SearchViewState([], "Custom empty message", false, false, false, true);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.Equal("No results found", presentation.Status.Title);
        Assert.Equal("Custom empty message", presentation.Status.Description);
        Assert.Equal("system-search-symbolic", presentation.Status.IconName);
    }

    [Fact]
    public void SearchVideoListSource_MapsErrorState()
    {
        var state = new SearchViewState([], "Search failed.", false, false, false, false);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.Equal("Could not complete search", presentation.Status.Title);
        Assert.Equal("Failed to load search results. Check your network connection and try again.",
            presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void SearchVideoListSource_MapsErrorStateWithCustomMessage()
    {
        var state = new SearchViewState([], "Search could not be completed.", false, false, false, false);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.Equal("Could not complete search", presentation.Status.Title);
        Assert.Equal("Search could not be completed.", presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void SearchVideoListSource_MapsLoadingState()
    {
        var state = new SearchViewState([], "Searching YouTube for “dotnet”…", true, false, false, true);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.Equal("Searching YouTube for “dotnet”…", presentation.LoadingMessage);
    }

    [Fact]
    public void HistoryVideoListSource_MapsSignedOutState()
    {
        var state = new HistoryViewState([], "", false, false,
            AuthenticatedHistoryStatus.AuthenticationRequired);
        var presentation = HistoryVideoListSource.MapState(state);

        Assert.Equal("Sign in to see history", presentation.Status.Title);
        Assert.Equal("Watch history requires an active YouTube session.", presentation.Status.Description);
        Assert.Equal("avatar-default-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
        Assert.Equal("Loading more history…", presentation.PaginationLoadingMessage);
    }

    [Fact]
    public void HistoryVideoListSource_MapsErrorState()
    {
        var state = new HistoryViewState([], "", false, false,
            AuthenticatedHistoryStatus.TemporaryBackendFailure);
        var presentation = HistoryVideoListSource.MapState(state);

        Assert.Equal("Could not load history", presentation.Status.Title);
        Assert.Equal("Failed to load your watch history. Check your network connection and try again.",
            presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void HistoryVideoListSource_MapsEmptyState()
    {
        var state = new HistoryViewState([], "", false, true,
            AuthenticatedHistoryStatus.Empty);
        var presentation = HistoryVideoListSource.MapState(state);

        Assert.Equal("No watch history", presentation.Status.Title);
        Assert.Equal("Videos you watch on YouTube will appear here.", presentation.Status.Description);
        Assert.Equal("document-open-recent-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void ChannelVideoListSource_MapsLoadingState()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "Loading Example…", true, true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.Equal("Loading Example…", presentation.LoadingMessage);
    }

    [Fact]
    public void ChannelVideoListSource_MapsLoadingStateWithDefaultMessage()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "", true, true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.Equal("Loading channel…", presentation.LoadingMessage);
    }

    [Fact]
    public void ChannelVideoListSource_MapsErrorState()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "Could not load channel.", false, false);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Equal("Could not load channel", presentation.Status.Title);
        Assert.Equal("Failed to load channel details. Check your network connection and try again.",
            presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void ChannelVideoListSource_MapsErrorStateWithCustomMessage()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "Channel not found", false, false);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Equal("Could not load channel", presentation.Status.Title);
        Assert.Equal("Channel not found", presentation.Status.Description);
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void ChannelVideoListSource_MapsEmptyState()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "", false, true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Equal("No videos found", presentation.Status.Title);
        Assert.Equal("This channel does not have any public videos available right now.",
            presentation.Status.Description);
        Assert.Equal("applications-internet-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void ChannelVideoListSource_MapsEmptyStateWithCustomMessage()
    {
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, "No public videos.", false, true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Equal("No videos found", presentation.Status.Title);
        Assert.Equal("No public videos.", presentation.Status.Description);
        Assert.Equal("applications-internet-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void ChannelVideoListSource_MapsReadyWithVideos()
    {
        var video = new VideoSummary("vid1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false);
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [video], ChannelVideoSort.Newest, "Showing 1 video from Example.", false, true,
            IsLoadingMore: true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Single(presentation.Videos);
        Assert.False(presentation.IsLoading);
        Assert.True(presentation.IsLoadingMore);
        Assert.Equal("Loading more videos…", presentation.PaginationLoadingMessage);
    }
}
