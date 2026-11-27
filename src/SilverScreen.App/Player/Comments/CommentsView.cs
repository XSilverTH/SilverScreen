using System.ComponentModel;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Account.Profile;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentsView : ViewBase<Box, CommentsViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<CommentsView>();

    private readonly AccountViewModel? _account;
    private readonly Action _closeRequested;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly Func<string, bool> _linkActivated;
    private readonly Dictionary<string, (string Text, string Markup)> _markupCache = new(StringComparer.Ordinal);
    private readonly Dictionary<Widget, CommentRowView> _rowsByCell = [];
    private readonly IThumbnailService? _thumbnails;
    private readonly Adjustment? _vadjustment;
    private readonly Dictionary<string, CommentRowState> _visibleCommentsById = new(StringComparer.Ordinal);
    private CancellationTokenSource? _composerAvatarCancellation;
    private Texture? _composerAvatarTexture;
    private string? _loadedComposerAvatarUrl;
    private string[] _displayedCommentIds = [];
    private CommentsViewState _state = new(CommentsViewStatus.Unavailable, [], string.Empty);

    public CommentsView(
        CommentsViewModel viewModel,
        Action closeRequested,
        Func<string, bool> linkActivated,
        IThumbnailService? thumbnails = null,
        AccountViewModel? account = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _linkActivated = linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));
        _thumbnails = thumbnails;
        _account = account;

        _vadjustment = comments_scrolled_window.Vadjustment;
        if (_vadjustment is not null)
            Lifetime.Track(
                () => _vadjustment.OnValueChanged += OnScrollValueChanged,
                () => _vadjustment.OnValueChanged -= OnScrollValueChanged);

        _itemIds = Lifetime.Own(StringList.New([]));
        var selection = Lifetime.Own(NoSelection.New(_itemIds));
        _factory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(
            () => _factory.OnSetup += OnRowSetup,
            () => _factory.OnSetup -= OnRowSetup);
        Lifetime.Track(
            () => _factory.OnBind += OnRowBind,
            () => _factory.OnBind -= OnRowBind);
        Lifetime.Track(
            () => _factory.OnUnbind += OnRowUnbind,
            () => _factory.OnUnbind -= OnRowUnbind);
        Lifetime.Track(
            () => _factory.OnTeardown += OnRowTeardown,
            () => _factory.OnTeardown -= OnRowTeardown);

        comments_list.Model = selection;
        comments_list.Factory = _factory;
        comments_stack.VisibleChildName = "unavailable";

        var composerBuffer = comments_composer_text.Buffer;
        if (composerBuffer is not null)
            Lifetime.Track(
                () => composerBuffer.OnChanged += OnComposerBufferChanged,
                () => composerBuffer.OnChanged -= OnComposerBufferChanged);

        if (_account is not null)
        {
            Lifetime.Track(
                () => _account.PropertyChanged += OnAccountPropertyChanged,
                () => _account.PropertyChanged -= OnAccountPropertyChanged);
            UpdateComposerAvatar();
        }

        Lifetime.Own(viewModel);
        ViewModel = viewModel;
    }

    protected override void BindViewModel(CommentsViewModel viewModel, BindingScope<CommentsViewModel> bindings)
    {
        bindings.Bind(
            nameof(CommentsViewModel.State),
            this,
            static vm => vm.State,
            static (view, state) => view.Render(state));
    }

    public void SetVideo(string? videoId)
    {
        ViewModel?.SetVideo(videoId);
    }

    public void EnsureLoaded()
    {
        ViewModel?.EnsureLoaded();
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        _closeRequested();
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        ViewModel?.Retry();
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || _vadjustment is null || !_state.HasMore || _state.IsLoadingMore ||
            _vadjustment.Value + _vadjustment.PageSize < _vadjustment.Upper - 280)
            return;

        ViewModel?.LoadMoreAsync().FireAndForget(Logger);
    }

    private void OnSortDropdownNotify(object? sender, EventArgs args)
    {
        ViewModel?.SetSortSelection(comments_sort_dropdown.GetSelected());
    }

    private void OnComposeButtonClicked(object? sender, EventArgs args)
    {
        var reveal = !comments_composer_revealer.RevealChild;
        comments_composer_revealer.RevealChild = reveal;
        if (reveal)
            comments_composer_text.GrabFocus();
    }

    private void OnComposerSubmitClicked(object? sender, EventArgs args)
    {
        var buffer = comments_composer_text.Buffer;
        if (buffer is null)
            return;

        buffer.GetBounds(out var start, out var end);
        var text = buffer.GetText(start, end, false);
        if (string.IsNullOrWhiteSpace(text))
            return;

        ViewModel?.AddComment(text, _account?.DisplayName, _account?.AvatarUrl);
        buffer.SetText(string.Empty, 0);
        comments_composer_revealer.RevealChild = false;
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        UpdateComposerAvatar();
    }

    private void UpdateComposerAvatar()
    {
        if (IsDisposed || _account is null)
            return;

        var isReady = _account.ProfileStatus == AccountProfilePresentationStatus.Ready;
        comments_composer_avatar.Text = isReady ? _account.DisplayName : string.Empty;
        comments_composer_avatar.ShowInitials = isReady;
        if (isReady && _thumbnails is not null && !string.IsNullOrWhiteSpace(_account.AvatarUrl))
            LoadComposerAvatarAsync(_account.AvatarUrl).FireAndForget(Logger);
        else
            ClearComposerAvatar();
    }

    private async Task LoadComposerAvatarAsync(string avatarUrl)
    {
        if (string.Equals(_loadedComposerAvatarUrl, avatarUrl, StringComparison.Ordinal))
            return;

        _loadedComposerAvatarUrl = avatarUrl;
        _composerAvatarCancellation?.Cancel();
        _composerAvatarCancellation?.Dispose();
        _composerAvatarCancellation = new CancellationTokenSource();
        var token = _composerAvatarCancellation.Token;

        try
        {
            var thumbnail = await _thumbnails!.GetThumbnailAsync(avatarUrl, token).ConfigureAwait(false);
            if (thumbnail is null || token.IsCancellationRequested || IsDisposed)
                return;

            var pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(thumbnail.LocalPath, 32, 32, true), token)
                .ConfigureAwait(false);
            if (pixbuf is null || token.IsCancellationRequested || IsDisposed)
            {
                pixbuf?.Dispose();
                return;
            }

            Lifetime.Idle(() =>
            {
                try
                {
                    if (token.IsCancellationRequested || IsDisposed ||
                        !string.Equals(_loadedComposerAvatarUrl, avatarUrl, StringComparison.Ordinal))
                        return false;

                    var texture = Texture.NewForPixbuf(pixbuf);
                    comments_composer_avatar.CustomImage = texture;
                    _composerAvatarTexture?.Dispose();
                    _composerAvatarTexture = texture;
                    return false;
                }
                finally
                {
                    pixbuf.Dispose();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Failed to load composer avatar");
        }
    }

    private void ClearComposerAvatar()
    {
        _composerAvatarCancellation?.Cancel();
        _composerAvatarCancellation?.Dispose();
        _composerAvatarCancellation = null;
        _loadedComposerAvatarUrl = null;
        comments_composer_avatar.CustomImage = null!;
        _composerAvatarTexture?.Dispose();
        _composerAvatarTexture = null;
    }

    private void OnComposerBufferChanged(object? sender, EventArgs args)
    {
        var buffer = comments_composer_text.Buffer;
        if (buffer is null)
            return;

        buffer.GetBounds(out var start, out var end);
        var text = buffer.GetText(start, end, false);
        comments_composer_placeholder.Visible = string.IsNullOrEmpty(text);
        comments_composer_submit_button.Sensitive = !string.IsNullOrWhiteSpace(text);
    }

    private void Render(CommentsViewState state)
    {
        _state = state;
        if (state.Status is CommentsViewStatus.Unavailable or CommentsViewStatus.Loading)
            _markupCache.Clear();

        ApplyComments(state.VisibleComments);

        comments_pagination_loading_revealer.RevealChild = state.IsLoadingMore;
        comments_pagination_loading_label.SetText(state.PaginationLoadingMessage);

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (state.Status)
        {
            case CommentsViewStatus.Unavailable:
                comments_stack.VisibleChildName = "unavailable";
                break;
            case CommentsViewStatus.Loading:
                comments_stack.VisibleChildName = "loading";
                break;
            case CommentsViewStatus.Error:
                comments_error_page.Description = state.StatusMessage;
                comments_stack.VisibleChildName = "error";
                break;
            case CommentsViewStatus.Empty:
                comments_empty_page.Description = state.StatusMessage;
                comments_stack.VisibleChildName = "empty";
                break;
            case CommentsViewStatus.List:
                comments_stack.VisibleChildName = "list";
                break;
        }
    }

    private void ApplyComments(IReadOnlyList<CommentRowState> visibleComments)
    {
        var nextIds = new string[visibleComments.Count];
        _visibleCommentsById.Clear();
        for (var i = 0; i < visibleComments.Count; i++)
        {
            var row = visibleComments[i];
            var id = row.Comment.Id;
            nextIds[i] = id;
            _visibleCommentsById[id] = row;
        }

        var prefixLength = 0;
        while (prefixLength < _displayedCommentIds.Length && prefixLength < nextIds.Length &&
               string.Equals(_displayedCommentIds[prefixLength], nextIds[prefixLength], StringComparison.Ordinal))
            prefixLength++;

        var suffixLength = 0;
        while (_displayedCommentIds.Length - suffixLength > prefixLength &&
               nextIds.Length - suffixLength > prefixLength &&
               string.Equals(_displayedCommentIds[_displayedCommentIds.Length - suffixLength - 1],
                   nextIds[nextIds.Length - suffixLength - 1], StringComparison.Ordinal))
            suffixLength++;

        var removedMiddleCount = _displayedCommentIds.Length - prefixLength - suffixLength;
        var addedMiddleCount = nextIds.Length - prefixLength - suffixLength;

        _displayedCommentIds = nextIds;
        if (removedMiddleCount != 0 || addedMiddleCount != 0)
        {
            var addedMiddleIds = new string[addedMiddleCount];
            for (var i = 0; i < addedMiddleCount; i++)
                addedMiddleIds[i] = nextIds[prefixLength + i];

            _itemIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleIds);
        }

        foreach (var row in _rowsByCell.Values)
            if (row.BoundCommentId is { } boundId && _visibleCommentsById.TryGetValue(boundId, out var rowState))
                BindRow(row, rowState);
    }

    private void BindRow(CommentRowView row, CommentRowState rowState)
    {
        var displayText = rowState.IsTranslated && !string.IsNullOrEmpty(rowState.TranslatedText)
            ? rowState.TranslatedText
            : rowState.Comment.Text;
        row.Bind(
            rowState.Comment,
            rowState.ReplyCount,
            rowState.RepliesExpanded,
            GetOrBuildMarkup(rowState.Comment.Id, displayText),
            rowState.CanTranslate,
            rowState.IsTranslated,
            rowState.IsTranslating,
            rowState.TranslatedText,
            rowState.TranslationError,
            rowState.Depth,
            rowState.IsFirstSibling,
            rowState.IsLastSibling,
            rowState.IsLastInThread,
            rowState.ThreadRootCommentId,
            rowState.AncestorHasMoreSiblings,
            rowState.HasMoreReplies,
            rowState.IsLoadingReplies,
            rowState.LoadMoreRepliesParentId);
    }

    private string GetOrBuildMarkup(string commentId, string text)
    {
        if (_markupCache.TryGetValue(commentId, out var cached) &&
            string.Equals(cached.Text, text, StringComparison.Ordinal))
            return cached.Markup;

        var markup = RichLabelMarkup.Build(text);
        _markupCache[commentId] = (text, markup);
        return markup;
    }

    private void OnRowSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        listItem.Focusable = true;
        var row = new CommentRowView(
            id => ViewModel?.ToggleReplies(id),
            _linkActivated,
            id => ViewModel?.ToggleTranslationAsync(id).FireAndForget(Logger),
            parentId => ViewModel?.LoadMoreRepliesAsync(parentId).FireAndForget(Logger),
            _thumbnails,
            id => ViewModel?.ToggleLikeAsync(id).FireAndForget(Logger));
        listItem.Child = row.Widget;
        _rowsByCell[row.Widget] = row;
    }

    private void OnRowBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject { String: { } id } } ||
            !_rowsByCell.TryGetValue(child, out var row) ||
            !_visibleCommentsById.TryGetValue(id, out var comment))
            return;

        BindRow(row, comment);
    }

    private void OnRowUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem { Child: { } child } && _rowsByCell.TryGetValue(child, out var row))
            row.Unbind();
    }

    private void OnRowTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child } || !_rowsByCell.Remove(child, out var row))
            return;

        row.Unbind();
        row.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var row in _rowsByCell.Values)
                row.Dispose();

            _rowsByCell.Clear();
            _visibleCommentsById.Clear();
            _markupCache.Clear();
        }

        base.Dispose(disposing);
    }
}