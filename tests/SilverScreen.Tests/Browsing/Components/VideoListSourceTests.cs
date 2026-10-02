using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;

namespace SilverScreen.Tests.Browsing.Components;

public sealed class VideoListSourceTests
{
    [Fact]
    public void HomeVideoListSource_MapsSignedOutState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.SignedOut, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.Equal("Sign in with Google or use cookies.txt to see your Home feed.", presentation.Status.Description);
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
    public void HomeVideoListSource_MapsRefreshingState()
    {
        var video = new VideoSummary("id1", "Title", "Channel", TimeSpan.FromMinutes(1),
            "https://example.com/thumb.jpg", false);
        var state = new HomeFeedState(HomeFeedStateKind.Ready, [video], IsLoading: true);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.Single(presentation.Videos);
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

        Assert.DoesNotContain("private-token", presentation.Status.Description);
        Assert.Equal("dialog-password-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void HomeVideoListSource_MapsSafeErrorState()
    {
        var state = new HomeFeedState(HomeFeedStateKind.SafeError, []);
        var presentation = HomeVideoListSource.MapState(state);

        Assert.Equal("Home", presentation.Status.Title);
        Assert.False(string.IsNullOrWhiteSpace(presentation.Status.Description));
        Assert.Equal("network-error-symbolic", presentation.Status.IconName);
        Assert.False(presentation.Status.ShowRetry);
    }

    [Fact]
    public void SearchVideoListSource_MapsSuccessEmptyState()
    {
        var state = new SearchViewState([], "Search complete.", false);
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
        var presentation = SearchVideoListSource.MapState(new SearchViewState([], "Custom empty message", false));
        Assert.Equal("Try different keywords or check spelling.", presentation.Status.Description);
    }

    [Fact]
    public void SearchVideoListSource_MapsErrorState()
    {
        var presentation = SearchVideoListSource.MapState(new SearchViewState([], "Search failed.", false, false, false, false));
        Assert.Equal("Could not complete search", presentation.Status.Title);
        Assert.True(presentation.Status.ShowRetry);
    }

    [Fact]
    public void SearchVideoListSource_KeepsBackendDiagnosticsOutOfEmptyAndFailureStatus()
    {
        var diagnostic = "private-token-" + "x".PadRight(4000, 'x');
        var failure = SearchVideoListSource.MapState(
            new SearchViewState([], diagnostic, false, false, false, false), diagnostic);
        var empty = SearchVideoListSource.MapState(
            new SearchViewState([], diagnostic, false, false, false, true));

        Assert.DoesNotContain("private-token", failure.Status.Description);
        Assert.DoesNotContain("private-token", failure.PaginationError);
        Assert.True(failure.Status.ShowRetry);
        Assert.DoesNotContain("private-token", empty.Status.Description);
        Assert.False(empty.Status.ShowRetry);
    }


    [Fact]
    public void SearchVideoListSource_MapsLoadingState()
    {
        var state = new SearchViewState([], "Searching YouTube for “dotnet”…", true);
        var presentation = SearchVideoListSource.MapState(state);

        Assert.True(presentation.IsLoading);
        Assert.DoesNotContain("dotnet", presentation.LoadingMessage);
    }

    [Fact]
    public void HistoryVideoListSource_MapsSignedOutState()
    {
        var state = new HistoryViewState([], "", false, false,
            AuthenticatedHistoryStatus.AuthenticationRequired);
        var presentation = HistoryVideoListSource.MapState(state);

        Assert.Equal("Sign in to see history", presentation.Status.Title);
        Assert.Equal("Sign in with Google or use cookies.txt to see your watch history.", presentation.Status.Description);
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
        Assert.DoesNotContain("private-token", presentation.Status.Description);
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
        Assert.DoesNotContain("Example", presentation.LoadingMessage);
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
    public void ChannelVideoListSource_UsesSafeFailureCopy()
    {
        var diagnostic = "private-token-" + "x".PadRight(4000, 'x');
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [], ChannelVideoSort.Newest, diagnostic, false, false);
        var presentation = ChannelVideoListSource.MapState(state, diagnostic);

        Assert.True(presentation.Status.ShowRetry);
        Assert.DoesNotContain("private-token", presentation.Status.Description);
        Assert.DoesNotContain("private-token", presentation.PaginationError);
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
    public void ChannelVideoListSource_MapsReadyWithVideos()
    {
        var video = new VideoSummary("vid1", "Title", "Channel", TimeSpan.FromMinutes(5), "thumb", false);
        var state = new ChannelViewState("https://www.youtube.com/@example", "Example", null, null, null,
            [video], ChannelVideoSort.Newest, "Showing 1 video from Example.", false, true,
            true);
        var presentation = ChannelVideoListSource.MapState(state);

        Assert.Single(presentation.Videos);
        Assert.False(presentation.IsLoading);
        Assert.True(presentation.IsLoadingMore);
        Assert.Equal("Loading more videos…", presentation.PaginationLoadingMessage);
    }
}