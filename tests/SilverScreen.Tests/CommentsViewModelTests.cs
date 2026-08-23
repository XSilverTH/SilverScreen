using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class CommentsViewModelTests
{
    [Fact]
    public async Task SuccessfulLoadProjectsTreeAndReplyExpansion()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");
        viewModel.EnsureLoaded();
        var request = Assert.Single(service.Requests);
        var root = Comment("root");
        var reply = Comment("reply", "root");
        var nestedReply = Comment("nested", "reply");
        request.Completion.SetResult(new YouTubeCommentsResult([root, reply, nestedReply], true, "Comments loaded."));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.Equal(["root"], viewModel.State.VisibleComments.Select(row => row.Comment.Id));
        Assert.Equal(1, viewModel.State.VisibleComments[0].ReplyCount);
        Assert.False(viewModel.State.VisibleComments[0].RepliesExpanded);

        viewModel.ToggleReplies("root");
        Assert.Equal(["root", "reply"], viewModel.State.VisibleComments.Select(row => row.Comment.Id));
        Assert.True(viewModel.State.VisibleComments[0].RepliesExpanded);
        Assert.Equal(1, viewModel.State.VisibleComments[1].ReplyCount);

        viewModel.ToggleReplies("reply");
        Assert.Equal(["root", "reply", "nested"], viewModel.State.VisibleComments.Select(row => row.Comment.Id));
    }

    [Fact]
    public async Task SortSelectionCancelsPriorLoadAndInvokesSelectedSort()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");
        viewModel.EnsureLoaded();
        var firstRequest = Assert.Single(service.Requests);
        Assert.Equal(YouTubeCommentSort.Top, firstRequest.Sort);

        viewModel.SetSortSelection(1);
        var secondRequest = Assert.IsType<ControlledCommentService.Pending>(service.Requests[1]);
        Assert.Equal(YouTubeCommentSort.Newest, secondRequest.Sort);
        Assert.True(firstRequest.Token.IsCancellationRequested);
        secondRequest.Completion.SetResult(new YouTubeCommentsResult([], true,
            "No comments were returned for this video."));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.Empty);
    }

    [Fact]
    public async Task StaleResultAfterVideoChangeIsIgnored()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");
        viewModel.EnsureLoaded();
        var firstRequest = Assert.Single(service.Requests);

        viewModel.SetVideo("bbbbbbbbbbb");
        viewModel.EnsureLoaded();
        var secondRequest = Assert.IsType<ControlledCommentService.Pending>(service.Requests[1]);
        secondRequest.Completion.SetResult(new YouTubeCommentsResult([Comment("new")], true, "Comments loaded."));
        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);

        firstRequest.Completion.SetResult(new YouTubeCommentsResult([Comment("old")], true, "Comments loaded."));
        await Task.Delay(25);
        Assert.Equal(["new"], viewModel.State.VisibleComments.Select(row => row.Comment.Id));
    }

    private static YouTubeComment Comment(string id, string? parentId = null)
    {
        return new YouTubeComment(id, id, id, "", 0, parentId);
    }

    private static async Task WaitForStateAsync(CommentsViewModel viewModel,
        Func<CommentsViewState, bool> predicate)
    {
        if (predicate(viewModel.State))
            return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        viewModel.StateChanged += OnStateChanged;
        try
        {
            if (!predicate(viewModel.State))
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            viewModel.StateChanged -= OnStateChanged;
        }

        return;

        void OnStateChanged(object? sender, CommentsViewState state)
        {
            if (predicate(state))
                completion.TrySetResult();
        }
    }

    private sealed class ControlledCommentService : IYouTubeCommentService
    {
        public List<Pending> Requests { get; } = [];

        public Task<YouTubeCommentsResult> GetCommentsAsync(string videoId, YouTubeCommentSort sort,
            CancellationToken cancellationToken = default)
        {
            var pending = new Pending(sort, cancellationToken);
            Requests.Add(pending);
            return pending.Completion.Task;
        }

        public sealed class Pending(YouTubeCommentSort sort, CancellationToken token)
        {
            public YouTubeCommentSort Sort { get; } = sort;
            public CancellationToken Token { get; } = token;
            public TaskCompletionSource<YouTubeCommentsResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}