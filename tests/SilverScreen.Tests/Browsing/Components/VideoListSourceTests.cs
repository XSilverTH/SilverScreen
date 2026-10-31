using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Browsing.History;

namespace SilverScreen.Tests.Browsing.Components;

public sealed class VideoListSourceTests
{
    [Fact]
    public void HomeFeedCoordinator_MapsSignedOutStatus()
    {
        var status = HomeFeedCoordinator.HomeSignedOutStatus(null);

        Assert.Equal("Home", status.Title);
        Assert.Equal("Sign in with Google or use cookies.txt to see your Home feed.", status.Description);
        Assert.Equal("avatar-default-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void HomeFeedCoordinator_MapsAuthenticationRequiredStatus()
    {
        var status = HomeFeedCoordinator.HomeAuthInvalidStatus(null, authenticationRejected: true);

        Assert.Equal("Home", status.Title);
        Assert.Equal(SilverScreen.Core.Account.Session.SessionGate.SessionNoLongerValidMessage, status.Description);
        Assert.Equal("dialog-password-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void HomeFeedCoordinator_MapsSafeErrorStatus()
    {
        var status = HomeFeedCoordinator.HomeErrorStatus();

        Assert.Equal("Home", status.Title);
        Assert.False(string.IsNullOrWhiteSpace(status.Description));
        Assert.Equal("network-error-symbolic", status.IconName);
        Assert.True(status.ShowRetry);
    }

    [Fact]
    public void HomeFeedCoordinator_MapsEmptyStatus()
    {
        var status = HomeFeedCoordinator.HomeEmptyStatus();

        Assert.Equal("Home", status.Title);
        Assert.Equal("No recommendations are available right now.", status.Description);
        Assert.Equal("applications-internet-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void SearchViewModel_MapsSuccessEmptyState()
    {
        var state = new FeedEngineState([], false, false, false, null, "Search complete.", true, null);
        var status = SearchViewModel.MapStatus(state);

        Assert.Equal("No results found", status.Title);
        Assert.Equal("Try different keywords or check spelling.", status.Description);
        Assert.Equal("system-search-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void SearchViewModel_MapsCustomEmptySummary()
    {
        var state = new FeedEngineState([], false, false, false, null, "Custom empty message", true, null);
        var status = SearchViewModel.MapStatus(state);

        Assert.Equal("Try different keywords or check spelling.", status.Description);
    }

    [Fact]
    public void SearchViewModel_MapsErrorState()
    {
        var state = new FeedEngineState([], false, false, false, null, "Search failed.", false, new Exception("Search failed."));
        var status = SearchViewModel.MapStatus(state);

        Assert.Equal("Could not complete search", status.Title);
        Assert.True(status.ShowRetry);
    }

    [Fact]
    public void SearchViewModel_KeepsBackendDiagnosticsOutOfEmptyAndFailureStatus()
    {
        var diagnostic = "private-token-" + "x".PadRight(4000, 'x');
        var failure = SearchViewModel.MapStatus(
            new FeedEngineState([], false, false, false, null, diagnostic, false, new Exception(diagnostic)));
        var empty = SearchViewModel.MapStatus(
            new FeedEngineState([], false, false, false, null, diagnostic, true, null));

        Assert.DoesNotContain("private-token", failure.Description);
        Assert.True(failure.ShowRetry);
        Assert.DoesNotContain("private-token", empty.Description);
        Assert.False(empty.ShowRetry);
    }

    [Fact]
    public void HistoryViewModel_MapsSignedOutState()
    {
        var state = new FeedEngineState([], false, false, false, null, "", false, null);
        var status = HistoryViewModel.MapStatus(AuthenticatedHistoryStatus.AuthenticationRequired, state);

        Assert.Equal("Sign in to see history", status.Title);
        Assert.Equal("Sign in with Google or use cookies.txt to see your watch history.", status.Description);
        Assert.Equal("avatar-default-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void HistoryViewModel_MapsErrorState()
    {
        var diagnostic = "private-token-" + "x".PadRight(4000, 'x');
        var state = new FeedEngineState([], false, false, false, null, diagnostic, false, new Exception(diagnostic));
        var status = HistoryViewModel.MapStatus(AuthenticatedHistoryStatus.TemporaryBackendFailure, state);

        Assert.Equal("Could not load history", status.Title);
        Assert.DoesNotContain("private-token", status.Description);
        Assert.Equal("network-error-symbolic", status.IconName);
        Assert.True(status.ShowRetry);
    }

    [Fact]
    public void HistoryViewModel_MapsEmptyState()
    {
        var state = new FeedEngineState([], false, false, false, null, "", true, null);
        var status = HistoryViewModel.MapStatus(AuthenticatedHistoryStatus.Empty, state);

        Assert.Equal("No watch history", status.Title);
        Assert.Equal("Videos you watch on YouTube will appear here.", status.Description);
        Assert.Equal("document-open-recent-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }

    [Fact]
    public void ChannelViewModel_UsesSafeFailureCopy()
    {
        var diagnostic = "private-token-" + "x".PadRight(4000, 'x');
        var state = new FeedEngineState([], false, false, false, null, diagnostic, false, new Exception(diagnostic));
        var status = ChannelViewModel.MapStatus(state);

        Assert.Equal("Could not load channel", status.Title);
        Assert.True(status.ShowRetry);
        Assert.DoesNotContain("private-token", status.Description);
        Assert.Equal("network-error-symbolic", status.IconName);
    }

    [Fact]
    public void ChannelViewModel_MapsEmptyState()
    {
        var state = new FeedEngineState([], false, false, false, null, "", true, null);
        var status = ChannelViewModel.MapStatus(state);

        Assert.Equal("No videos found", status.Title);
        Assert.Equal("This channel does not have any public videos available right now.",
            status.Description);
        Assert.Equal("applications-internet-symbolic", status.IconName);
        Assert.False(status.ShowRetry);
    }
}