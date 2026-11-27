using SilverScreen.Core.Player.Comments;
using SilverScreen.Player.Comments;

namespace SilverScreen.Tests.Player.Comments;

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

    [Fact]
    public async Task LoadMoreAsync_WhenHasMore_RequestsOnlyTheNextContinuationPage()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");
        viewModel.EnsureLoaded();

        var firstRequest = Assert.Single(service.Requests);
        Assert.Equal(CommentsViewModel.InitialPageSize, firstRequest.Count);
        Assert.True(firstRequest.IsFirstPage);
        var initialComments = Enumerable.Range(1, CommentsViewModel.InitialPageSize)
            .Select(i => Comment($"c_{i}"))
            .ToArray();
        firstRequest.Completion.SetResult(new YouTubeCommentsResult(initialComments, true, "Comments loaded.", true));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.True(viewModel.State.HasMore);
        Assert.False(viewModel.State.IsLoadingMore);
        Assert.Equal(CommentsViewModel.InitialPageSize, viewModel.State.VisibleComments.Count);

        var loadMoreTask = viewModel.LoadMoreAsync();
        await WaitForStateAsync(viewModel, state => state.IsLoadingMore);

        Assert.Equal(2, service.Requests.Count);
        var secondRequest = service.Requests[1];
        Assert.Equal(CommentsViewModel.PageSizeIncrement, secondRequest.Count);
        Assert.False(secondRequest.IsFirstPage);

        var expandedComments = Enumerable
            .Range(1, CommentsViewModel.InitialPageSize + CommentsViewModel.PageSizeIncrement)
            .Select(i => Comment($"c_{i}"))
            .ToArray();
        secondRequest.Completion.SetResult(new YouTubeCommentsResult(expandedComments, true, "Comments loaded."));

        await loadMoreTask;
        await WaitForStateAsync(viewModel, state => !state.IsLoadingMore);

        Assert.False(viewModel.State.HasMore);
        Assert.Equal(CommentsViewModel.InitialPageSize + CommentsViewModel.PageSizeIncrement,
            viewModel.State.VisibleComments.Count);
    }

    [Fact]
    public async Task SetVideo_AutomaticallyPreloadsCommentsWithoutRequiringEnsureLoaded()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);

        viewModel.SetVideo("aaaaaaaaaaa");

        var request = Assert.Single(service.Requests);
        Assert.Equal(YouTubeCommentSort.Top, request.Sort);
        Assert.Equal(CommentsViewModel.InitialPageSize, request.Count);
        Assert.Equal(CommentsViewStatus.Loading, viewModel.State.Status);

        var root = Comment("root");
        request.Completion.SetResult(new YouTubeCommentsResult([root], true, "Comments loaded."));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.Equal(["root"], viewModel.State.VisibleComments.Select(row => row.Comment.Id));

        // EnsureLoaded is a no-op when preloading has already completed
        viewModel.EnsureLoaded();
        Assert.Single(service.Requests);
    }

    [Fact]
    public void SetVideo_WithNullOrInvalidVideoId_DoesNotPreloadAndSetsUnavailable()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);

        viewModel.SetVideo(null);
        Assert.Empty(service.Requests);
        Assert.Equal(CommentsViewStatus.Unavailable, viewModel.State.Status);

        viewModel.SetVideo("invalid_short");
        Assert.Empty(service.Requests);
        Assert.Equal(CommentsViewStatus.Unavailable, viewModel.State.Status);
    }

    [Fact]
    public void SetVideo_WithSameVideoId_DoesNotDuplicateRequest()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);

        viewModel.SetVideo("aaaaaaaaaaa");
        Assert.Single(service.Requests);

        viewModel.SetVideo("aaaaaaaaaaa");
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task Retry_AfterFailure_ReloadsCommentsForCurrentVideo()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);

        viewModel.SetVideo("aaaaaaaaaaa");
        Assert.Single(service.Requests);

        service.Requests[0].Completion.SetResult(new YouTubeCommentsResult([], false, "Network error"));
        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.Error);
        Assert.Equal(CommentsViewStatus.Error, viewModel.State.Status);

        viewModel.Retry();
        Assert.Equal(2, service.Requests.Count);

        service.Requests[1].Completion.SetResult(new YouTubeCommentsResult([Comment("c1")], true, ""));
        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.Equal(CommentsViewStatus.List, viewModel.State.Status);
    }

    [Fact]
    public async Task ToggleTranslationAsync_TranslatesAndTogglesBackToOriginalWithoutRefetching()
    {
        var service = new ControlledCommentService
        {
            TranslateHandler = (commentId, text, targetLanguage) =>
                Task.FromResult(new YouTubeCommentTranslationResult(true, $"[translated:{targetLanguage}] {text}"))
        };
        var prefs = new StubPreferencesService(new Core.Preferences.AppPreferences { YouTubeLanguage = "es" });
        using var viewModel = new CommentsViewModel(service, prefs);

        viewModel.SetVideo("dQw4w9WgXcQ");
        var request = Assert.Single(service.Requests);
        request.Completion.SetResult(new YouTubeCommentsResult(
            [new YouTubeComment("c1", "Author", "This is a great video and I love it", "1d ago", 5)],
            true,
            "Loaded"));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.True(viewModel.State.VisibleComments[0].CanTranslate);
        Assert.False(viewModel.State.VisibleComments[0].IsTranslated);

        await viewModel.ToggleTranslationAsync("c1");

        var translatedRow = viewModel.State.VisibleComments[0];
        Assert.True(translatedRow.IsTranslated);
        Assert.Equal("[translated:es] This is a great video and I love it", translatedRow.TranslatedText);
        Assert.Equal(1, service.TranslateCallCount);

        // Toggling again shows original without another service call
        await viewModel.ToggleTranslationAsync("c1");
        Assert.False(viewModel.State.VisibleComments[0].IsTranslated);
        Assert.Equal(1, service.TranslateCallCount);
    }

    [Fact]
    public async Task ToggleTranslationAsync_OnFailure_PublishesTranslationError()
    {
        var service = new ControlledCommentService
        {
            TranslateHandler = (_, _, _) =>
                Task.FromResult(new YouTubeCommentTranslationResult(false, ErrorMessage: "Could not translate comment."))
        };
        using var viewModel = new CommentsViewModel(service);

        viewModel.SetVideo("dQw4w9WgXcQ");
        var request = Assert.Single(service.Requests);
        request.Completion.SetResult(new YouTubeCommentsResult(
            [new YouTubeComment("c1", "Author", "Hola amigos, gracias", "1d ago", 5)],
            true,
            "Loaded"));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        await viewModel.ToggleTranslationAsync("c1");

        var row = viewModel.State.VisibleComments[0];
        Assert.False(row.IsTranslated);
        Assert.Equal("Could not translate comment.", row.TranslationError);
    }

    [Fact]
    public async Task ToggleReplies_WhenHasMoreRepliesAndNotYetLoaded_LoadsRepliesOnDemandAndProjectsTree()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");

        var initialRequest = Assert.Single(service.Requests);
        var root = new YouTubeComment("root", "RootAuthor", "Top comment", "1d ago", 10, null, ReplyCount: 3,
            HasMoreReplies: true);
        initialRequest.Completion.SetResult(new YouTubeCommentsResult([root], true, "Comments loaded."));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        Assert.Equal(3, viewModel.State.VisibleComments[0].ReplyCount);
        Assert.True(viewModel.State.VisibleComments[0].HasMoreReplies);
        Assert.False(viewModel.State.VisibleComments[0].RepliesExpanded);

        viewModel.ToggleReplies("root");
        var replyRequest = Assert.Single(service.ReplyRequests);
        Assert.Equal("root", replyRequest.ParentCommentId);
        Assert.True(viewModel.State.VisibleComments[0].IsLoadingReplies);
        Assert.True(viewModel.State.VisibleComments[0].RepliesExpanded);

        var updatedRoot = root with { HasMoreReplies = true };
        var reply1 = Comment("reply1", "root");
        var reply2 = Comment("reply2", "root");
        replyRequest.Completion.SetResult(new YouTubeCommentsResult([updatedRoot, reply1, reply2], true,
            "Replies loaded."));

        await WaitForStateAsync(viewModel, state =>
            state.VisibleComments.Count == 3 && !state.VisibleComments[0].IsLoadingReplies);

        Assert.Equal(["root", "reply1", "reply2"], viewModel.State.VisibleComments.Select(r => r.Comment.Id));
        Assert.Equal(0, viewModel.State.VisibleComments[0].Depth);
        Assert.Equal(1, viewModel.State.VisibleComments[1].Depth);
        Assert.False(viewModel.State.VisibleComments[1].IsLastSibling);
        Assert.Null(viewModel.State.VisibleComments[1].LoadMoreRepliesParentId);
        Assert.Equal(1, viewModel.State.VisibleComments[2].Depth);
        Assert.True(viewModel.State.VisibleComments[2].IsLastSibling);
        Assert.Equal("root", viewModel.State.VisibleComments[2].LoadMoreRepliesParentId);

        var loadMoreTask = viewModel.LoadMoreRepliesAsync("root");
        Assert.Equal(2, service.ReplyRequests.Count);
        var secondReplyRequest = service.ReplyRequests[1];
        var finalRoot = updatedRoot with { HasMoreReplies = false };
        var reply3 = Comment("reply3", "root");
        secondReplyRequest.Completion.SetResult(new YouTubeCommentsResult([finalRoot, reply1, reply2, reply3], true,
            "Replies loaded."));
        await loadMoreTask;

        Assert.Equal(["root", "reply1", "reply2", "reply3"], viewModel.State.VisibleComments.Select(r => r.Comment.Id));
        Assert.False(viewModel.State.VisibleComments[0].HasMoreReplies);
        Assert.Null(viewModel.State.VisibleComments[3].LoadMoreRepliesParentId);
    }

    [Fact]
    public async Task BuildVisibleComments_ProjectsAncestorLinesForMultiLevelTree()
    {
        var service = new ControlledCommentService();
        using var viewModel = new CommentsViewModel(service);
        viewModel.SetVideo("aaaaaaaaaaa");

        var request = Assert.Single(service.Requests);
        var root = Comment("root");
        var reply1 = Comment("reply1", "root");
        var reply2 = Comment("reply2", "root");
        var nested = Comment("nested", "reply1");
        request.Completion.SetResult(new YouTubeCommentsResult([root, reply1, reply2, nested], true,
            "Comments loaded."));

        await WaitForStateAsync(viewModel, state => state.Status == CommentsViewStatus.List);
        viewModel.ToggleReplies("root");
        viewModel.ToggleReplies("reply1");

        Assert.Equal(["root", "reply1", "nested", "reply2"],
            viewModel.State.VisibleComments.Select(r => r.Comment.Id));

        var nestedRow = viewModel.State.VisibleComments[2];
        Assert.Equal(2, nestedRow.Depth);
        Assert.True(nestedRow.IsLastSibling);
        Assert.Equal([true], nestedRow.AncestorHasMoreSiblings);
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
        public List<PendingReplies> ReplyRequests { get; } = [];
        public int TranslateCallCount { get; private set; }
        public Func<string, string, string?, Task<YouTubeCommentTranslationResult>>? TranslateHandler { get; init; }

        public Task<YouTubeCommentsResult> LoadFirstPageAsync(
            string videoId,
            YouTubeCommentSort sort,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            var pending = new Pending(videoId, sort, count, cancellationToken, true);
            Requests.Add(pending);
            return pending.Completion.Task;
        }

        public Task<YouTubeCommentsResult> LoadNextPageAsync(
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            var pending = new Pending(null, YouTubeCommentSort.Top, count, cancellationToken, false);
            Requests.Add(pending);
            return pending.Completion.Task;
        }

        public Task<YouTubeCommentsResult> LoadRepliesAsync(
            string parentCommentId,
            CancellationToken cancellationToken = default)
        {
            var pending = new PendingReplies(parentCommentId, cancellationToken);
            ReplyRequests.Add(pending);
            return pending.Completion.Task;
        }

        public Task<YouTubeCommentTranslationResult> TranslateCommentAsync(
            string commentId,
            string text,
            string? targetLanguage = null,
            CancellationToken cancellationToken = default)
        {
            TranslateCallCount++;
            return TranslateHandler is not null
                ? TranslateHandler(commentId, text, targetLanguage)
                : Task.FromResult(new YouTubeCommentTranslationResult(false, ErrorMessage: "Not configured"));
        }

        public sealed class Pending(
            string? videoId,
            YouTubeCommentSort sort,
            int count,
            CancellationToken token,
            bool isFirstPage)
        {
            public string? VideoId { get; } = videoId;
            public YouTubeCommentSort Sort { get; } = sort;
            public int Count { get; } = count;
            public CancellationToken Token { get; } = token;
            public bool IsFirstPage { get; } = isFirstPage;

            public TaskCompletionSource<YouTubeCommentsResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public sealed class PendingReplies(string parentCommentId, CancellationToken token)
        {
            public string ParentCommentId { get; } = parentCommentId;
            public CancellationToken Token { get; } = token;

            public TaskCompletionSource<YouTubeCommentsResult> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class StubPreferencesService(Core.Preferences.AppPreferences preferences)
        : Core.Preferences.IPreferencesService
    {
        public event EventHandler<Core.Preferences.AppPreferences>? PreferencesChanged;
        public Core.Preferences.AppPreferences GetPreferences() => preferences;

        public void SavePreferences(Core.Preferences.AppPreferences updated)
        {
            preferences = updated;
            PreferencesChanged?.Invoke(this, updated);
        }
    }
}