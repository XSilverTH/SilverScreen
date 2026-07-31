using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_RendersTheServerOrderedFirstPage()
    {
        var service = new FakeHistoryService
        {
            FirstPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("newest"), CreateVideo("older")], "next"),
                "Watch history loaded.")
        };
        var reporter = new FakeStatusReporter();
        using var viewModel = new HistoryViewModel(service, reporter);

        await viewModel.LoadAsync();

        Assert.Equal(["newest", "older"], viewModel.State.Videos.Select(video => video.Id));
        Assert.True(viewModel.State.HasMore);
        Assert.Equal(AuthenticatedHistoryStatus.Success, viewModel.State.Status);
        Assert.Equal("Watch history loaded.", reporter.LastStatus);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsTheNextServerPageWithoutDuplicatingVideos()
    {
        var service = new FakeHistoryService
        {
            FirstPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v1")], "next"),
                "Watch history loaded."),
            NextPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage([CreateVideo("v1"), CreateVideo("v2")], null),
                "Watch history loaded.")
        };
        using var viewModel = new HistoryViewModel(service, new FakeStatusReporter());
        await viewModel.LoadAsync();

        await viewModel.LoadMoreAsync();

        Assert.Equal(1, service.NextPageCallCount);
        Assert.Equal(["v1", "v2"], viewModel.State.Videos.Select(video => video.Id));
        Assert.False(viewModel.State.HasMore);
    }

    [Fact]
    public async Task LoadAsync_ExposesAuthenticationRequiredState()
    {
        var service = new FakeHistoryService
        {
            FirstPage = new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.AuthenticationRequired,
                FeedPage.Empty,
                "Sign in to YouTube to load your watch history.")
        };
        using var viewModel = new HistoryViewModel(service, new FakeStatusReporter());

        await viewModel.LoadAsync();

        Assert.False(viewModel.State.IsSuccess);
        Assert.Equal(AuthenticatedHistoryStatus.AuthenticationRequired, viewModel.State.Status);
        Assert.Empty(viewModel.State.Videos);
    }

    private static VideoSummary CreateVideo(string id)
    {
        return new VideoSummary(id, $"Video {id}", "Channel", TimeSpan.FromMinutes(3), "thumbnail", false);
    }

    private sealed class FakeHistoryService : IAuthenticatedHistoryService
    {
        public AuthenticatedHistoryResult FirstPage { get; init; } = new(
            AuthenticatedHistoryStatus.Empty, FeedPage.Empty, "No watch history was returned.");
        public AuthenticatedHistoryResult NextPage { get; init; } = new(
            AuthenticatedHistoryStatus.Empty, FeedPage.Empty, "No additional watch history is available.");
        public int NextPageCallCount { get; private set; }

        public Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(FirstPage);
        }

        public Task<AuthenticatedHistoryResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
        {
            NextPageCallCount++;
            return Task.FromResult(NextPage);
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
