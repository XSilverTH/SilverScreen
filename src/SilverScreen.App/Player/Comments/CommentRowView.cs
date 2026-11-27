using Cairo;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentRowView : ViewBase<Box>
{
    private const int AvatarSize = 32;
    private const int FirstLevelIndent = 56;
    private const int IndentPerDepth = 24;
    private const int MaxVisualDepth = 4;
    private static readonly ILogger Logger = Log.ForContext<CommentRowView>();

    private readonly NativeMemoryPressure _avatarPressure = new();
    private readonly Func<string, bool> _linkActivated;
    private readonly Action<string>? _likeRequested;
    private readonly Action<string>? _loadMoreRepliesRequested;
    private readonly Action<string> _repliesToggleRequested;
    private readonly IThumbnailService? _thumbnails;
    private readonly Action<string>? _translateRequested;

    private CancellationTokenSource? _avatarCancellation;
    private Texture? _avatarTexture;
    private string? _loadedAvatarUrl;

    private IReadOnlyList<bool> _boundAncestorHasMoreSiblings = [];
    private bool? _boundCanTranslate;
    private int _boundDepth;
    private bool? _boundHasMoreReplies;
    private bool _boundIsFirstSibling = true;
    private bool _boundIsLastInThread = true;
    private bool _boundIsLastSibling = true;
    private bool? _boundIsLoadingReplies;
    private bool? _boundIsTranslated;
    private bool? _boundIsTranslating;
    private string? _boundLoadMoreRepliesParentId;
    private bool? _boundRepliesVisible;
    private int _boundReplyCount = -1;
    private string? _boundText;
    private string? _boundThreadRootCommentId;
    private string? _boundTranslationError;

    public CommentRowView(
        Action<string> repliesToggleRequested,
        Func<string, bool> linkActivated,
        Action<string>? translateRequested = null,
        Action<string>? loadMoreRepliesRequested = null,
        IThumbnailService? thumbnails = null,
        Action<string>? likeRequested = null)
    {
        _repliesToggleRequested =
            repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));
        _linkActivated = linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));
        _translateRequested = translateRequested;
        _loadMoreRepliesRequested = loadMoreRepliesRequested;
        _thumbnails = thumbnails;
        _likeRequested = likeRequested;

        comment_tree_gutter.SetDrawFunc(DrawTreeLines);
        Lifetime.Track(() => { }, CancelAvatarLoad);
    }

    internal string? BoundCommentId { get; private set; }

    private async Task LoadAvatarAsync(string avatarUrl)
    {
        if (string.Equals(_loadedAvatarUrl, avatarUrl, StringComparison.Ordinal))
            return;

        ClearAvatar();
        _loadedAvatarUrl = avatarUrl;
        if (_thumbnails is null)
            return;

        var cts = new CancellationTokenSource();
        _avatarCancellation = cts;
        var ct = cts.Token;

        Pixbuf? pixbuf = null;
        try
        {
            var thumbnail = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (thumbnail is not null)
                pixbuf = await Task.Run(
                    () => Pixbuf.NewFromFileAtScale(thumbnail.LocalPath, AvatarSize, AvatarSize, true),
                    ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                Logger.Warning(ex, "Failed comment avatar load");
            return;
        }

        if (pixbuf is null || IsDisposed || ct.IsCancellationRequested)
        {
            pixbuf?.Dispose();
            return;
        }

        var decoded = pixbuf;
        Lifetime.Idle(() =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested)
                    return false;

                var texture = Texture.NewForPixbuf(decoded);
                comment_avatar.CustomImage = texture;
                _avatarTexture?.Dispose();
                _avatarTexture = texture;
                _avatarPressure.Track(AvatarSize * AvatarSize * 4);
            }
            finally
            {
                decoded.Dispose();
            }

            return false;
        });
    }

    private void ClearAvatar()
    {
        CancelAvatarLoad();
        _loadedAvatarUrl = null;
        comment_avatar.CustomImage = null!;
        _avatarTexture?.Dispose();
        _avatarTexture = null;
    }

    private void CancelAvatarLoad()
    {
        if (_avatarCancellation is { } cts)
        {
            _avatarCancellation = null;
            cts.Cancel();
            cts.Dispose();
        }
    }

    private bool OnCommentTextActivateLink(Label sender, Label.ActivateLinkSignalArgs args)
    {
        return _linkActivated(args.Uri);
    }

    private void OnRepliesButtonClicked(object? sender, EventArgs args)
    {
        var targetId = _boundDepth == 0 ? BoundCommentId : _boundThreadRootCommentId;
        if (targetId is not null)
            _repliesToggleRequested(targetId);
    }

    private void OnNestedRepliesButtonClicked(object? sender, EventArgs args)
    {
        if (BoundCommentId is { } commentId)
            _repliesToggleRequested(commentId);
    }

    private void OnLoadMoreRepliesButtonClicked(object? sender, EventArgs args)
    {
        if (_boundLoadMoreRepliesParentId is { } parentId)
            _loadMoreRepliesRequested?.Invoke(parentId);
    }

    private void OnTranslateButtonClicked(object? sender, EventArgs args)
    {
        if (BoundCommentId is { } commentId)
            _translateRequested?.Invoke(commentId);
    }

    public void Bind(YouTubeComment comment, int replyCount, bool repliesVisible)
    {
        Bind(comment, replyCount, repliesVisible, null);
    }

    internal void Bind(
        YouTubeComment comment,
        int replyCount,
        bool repliesVisible,
        string? prebuiltMarkup,
        bool canTranslate = false,
        bool isTranslated = false,
        bool isTranslating = false,
        string? translatedText = null,
        string? translationError = null,
        int depth = 0,
        bool isFirstSibling = true,
        bool isLastSibling = true,
        bool isLastInThread = true,
        string? threadRootCommentId = null,
        IReadOnlyList<bool>? ancestorHasMoreSiblings = null,
        bool hasMoreReplies = false,
        bool isLoadingReplies = false,
        string? loadMoreRepliesParentId = null)
    {
        var displayText = isTranslated && !string.IsNullOrEmpty(translatedText)
            ? translatedText
            : comment.Text;
        var sameId = string.Equals(BoundCommentId, comment.Id, StringComparison.Ordinal);
        var sameText = string.Equals(_boundText, displayText, StringComparison.Ordinal);

        BoundCommentId = comment.Id;
        if (!sameId)
        {
            comment_author_label.SetText(comment.AuthorName);
            comment_avatar.Text = comment.AuthorName;
            comment_published_time_label.SetText(comment.PublishedTimeText);
            comment_published_time_label.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));

            if (_thumbnails is not null && !string.IsNullOrWhiteSpace(comment.AuthorAvatarUrl))
                LoadAvatarAsync(comment.AuthorAvatarUrl).FireAndForget(Logger);
            else
                ClearAvatar();
        }

        UpdateLikeButton(comment);

        var previousIsLastInThread = _boundIsLastInThread;
        _boundDepth = Math.Max(0, depth);
        _boundIsFirstSibling = isFirstSibling;
        _boundIsLastSibling = isLastSibling;
        _boundIsLastInThread = isLastInThread;
        _boundThreadRootCommentId = threadRootCommentId;
        _boundAncestorHasMoreSiblings = ancestorHasMoreSiblings ?? [];

        var isTopLevel = _boundDepth == 0;

        var effectiveDepth = Math.Min(_boundDepth, MaxVisualDepth);
        comment_tree_gutter.SetVisible(effectiveDepth > 0);
        comment_content_box.SetMarginStart(effectiveDepth > 0 ? 0 : 12);
        comment_content_box.SetMarginTop(effectiveDepth > 0 ? 10 : 12);
        comment_content_box.SetMarginBottom(effectiveDepth > 0 ? 10 : 12);
        if (effectiveDepth > 0)
        {
            var gutterWidth = FirstLevelIndent + (effectiveDepth - 1) * IndentPerDepth;
            comment_tree_gutter.SetContentWidth(gutterWidth);
            comment_tree_gutter.QueueDraw();
        }

        UpdateCardShapeClasses(isTopLevel, isLastInThread);

        if (!sameId || !sameText)
        {
            _boundText = displayText;
            SetCommentText(displayText, prebuiltMarkup);
        }

        if (!sameId ||
            _boundCanTranslate != canTranslate ||
            _boundIsTranslated != isTranslated ||
            _boundIsTranslating != isTranslating ||
            !string.Equals(_boundTranslationError, translationError, StringComparison.Ordinal))
        {
            _boundCanTranslate = canTranslate;
            _boundIsTranslated = isTranslated;
            _boundIsTranslating = isTranslating;
            _boundTranslationError = translationError;

            comment_translate_button.SetVisible(canTranslate);
            comment_translate_button.SetSensitive(!isTranslating);
            if (isTranslated)
                comment_translate_button.AddCssClass("accent");
            else
                comment_translate_button.RemoveCssClass("accent");

            comment_translate_button.SetTooltipText(isTranslating
                ? "Translating…"
                : !string.IsNullOrWhiteSpace(translationError)
                    ? translationError
                    : isTranslated
                        ? "Show original"
                        : "Translate comment");
        }

        var showLoadMore = !string.IsNullOrEmpty(loadMoreRepliesParentId);
        if (!sameId || !string.Equals(_boundLoadMoreRepliesParentId, loadMoreRepliesParentId, StringComparison.Ordinal))
        {
            _boundLoadMoreRepliesParentId = loadMoreRepliesParentId;
            comment_load_more_replies_button.SetVisible(showLoadMore);
        }

        var showNestedToggle = !isTopLevel && (replyCount > 0 || hasMoreReplies);
        if (!sameId ||
            previousIsLastInThread != isLastInThread ||
            _boundReplyCount != replyCount ||
            _boundRepliesVisible != repliesVisible ||
            _boundHasMoreReplies != hasMoreReplies ||
            _boundIsLoadingReplies != isLoadingReplies)
        {
            _boundReplyCount = replyCount;
            _boundRepliesVisible = repliesVisible;
            _boundHasMoreReplies = hasMoreReplies;
            _boundIsLoadingReplies = isLoadingReplies;

            // Top-level footer bar: shown on collapsed top-level with replies,
            // or on the last row of an expanded thread.
            var showFooterBar = (isTopLevel && (replyCount > 0 || hasMoreReplies) && isLastInThread) ||
                                (!isTopLevel && isLastInThread);
            comment_replies_button.SetVisible(showFooterBar);
            comment_replies_button.SetSensitive(isTopLevel ? !isLoadingReplies : true);
            if (showFooterBar)
            {
                var isExpandedBar = !isTopLevel || repliesVisible;
                comment_replies_chevron.SetFromIconName(isExpandedBar
                    ? "pan-up-symbolic"
                    : "pan-down-symbolic");
                comment_replies_label.SetText(isTopLevel && isLoadingReplies
                    ? "Loading replies…"
                    : isExpandedBar
                        ? "Hide replies"
                        : replyCount > 0
                            ? $"Show {FormatReplyCount(replyCount)}"
                            : "Show replies");
            }
            else
            {
                comment_replies_label.SetText(string.Empty);
            }

            comment_thread_divider.SetVisible(isTopLevel && !isLastInThread);

            comment_nested_replies_button.SetVisible(showNestedToggle);
            comment_nested_replies_button.SetSensitive(!isLoadingReplies);
            if (showNestedToggle)
            {
                comment_nested_replies_chevron.SetFromIconName(repliesVisible
                    ? "pan-up-symbolic"
                    : "pan-down-symbolic");
                comment_nested_replies_label.SetText(isLoadingReplies
                    ? "Loading replies…"
                    : repliesVisible
                        ? "Hide replies"
                        : replyCount > 0
                            ? $"Show {FormatReplyCount(replyCount)}"
                            : "Show replies");
            }
            else
            {
                comment_nested_replies_label.SetText(string.Empty);
            }
        }

        comment_inline_actions_box.SetVisible(showNestedToggle || showLoadMore);
    }

    public void Unbind()
    {
        BoundCommentId = null;
        _boundText = null;
        _boundReplyCount = -1;
        _boundRepliesVisible = null;
        _boundHasMoreReplies = null;
        _boundIsLoadingReplies = null;
        _boundLoadMoreRepliesParentId = null;
        _boundThreadRootCommentId = null;
        _boundCanTranslate = null;
        _boundIsTranslated = null;
        _boundIsTranslating = null;
        _boundTranslationError = null;
        _boundDepth = 0;
        _boundIsFirstSibling = true;
        _boundIsLastSibling = true;
        _boundIsLastInThread = true;
        _boundAncestorHasMoreSiblings = [];
        comment_card.RemoveCssClass("comment-row-top");
        comment_card.RemoveCssClass("comment-row-middle");
        comment_card.RemoveCssClass("comment-row-bottom");
        comment_tree_gutter.SetVisible(false);
        comment_content_box.SetMarginStart(12);
        comment_content_box.SetMarginTop(12);
        comment_content_box.SetMarginBottom(12);
        comment_author_label.SetText(string.Empty);
        comment_published_time_label.SetText(string.Empty);
        comment_published_time_label.SetVisible(false);
        comment_text_label.SetText(string.Empty);
        comment_translate_button.SetVisible(false);
        comment_translate_button.SetSensitive(true);
        comment_translate_button.RemoveCssClass("accent");
        comment_header_likes_label.SetText(string.Empty);
        comment_like_icon.SetFromResource("/SilverScreen/Assets/like-symbolic.svg");
        comment_like_button.SetSensitive(true);
        comment_like_button.RemoveCssClass("accent");
        comment_like_button.SetTooltipText("Like comment");
        comment_thread_divider.SetVisible(false);
        comment_replies_button.SetVisible(false);
        comment_replies_button.SetSensitive(true);
        comment_replies_label.SetText(string.Empty);
        comment_nested_replies_button.SetVisible(false);
        comment_nested_replies_button.SetSensitive(true);
        comment_nested_replies_label.SetText(string.Empty);
        comment_load_more_replies_button.SetVisible(false);
        comment_inline_actions_box.SetVisible(false);
    }

    private void UpdateLikeButton(YouTubeComment comment)
    {
        comment_header_likes_label.SetText(FormatCount(comment.LikeCount));
        var canToggle = _likeRequested is not null &&
                        !string.IsNullOrWhiteSpace(comment.IsLiked ? comment.UnlikeAction : comment.LikeAction);
        comment_like_button.SetSensitive(canToggle);
        comment_like_button.SetTooltipText(comment.IsLiked ? "Unlike comment" : "Like comment");
        comment_like_icon.SetFromResource(comment.IsLiked
            ? "/SilverScreen/Assets/liked-symbolic.svg"
            : "/SilverScreen/Assets/like-symbolic.svg");

        if (comment.IsLiked)
            comment_like_button.AddCssClass("accent");
        else
            comment_like_button.RemoveCssClass("accent");
    }

    private void OnLikeButtonClicked(object? sender, EventArgs args)
    {
        if (!string.IsNullOrEmpty(BoundCommentId))
            _likeRequested?.Invoke(BoundCommentId);
    }

    private void UpdateCardShapeClasses(bool isTopLevel, bool isLastInThread)
    {
        comment_card.RemoveCssClass("comment-row-top");
        comment_card.RemoveCssClass("comment-row-middle");
        comment_card.RemoveCssClass("comment-row-bottom");

        if (isTopLevel && !isLastInThread)
            comment_card.AddCssClass("comment-row-top");
        else if (!isTopLevel && !isLastInThread)
            comment_card.AddCssClass("comment-row-middle");
        else if (!isTopLevel && isLastInThread)
            comment_card.AddCssClass("comment-row-bottom");
    }

    private void DrawTreeLines(DrawingArea area, Context cr, int width, int height)
    {
        var effectiveDepth = Math.Min(_boundDepth, MaxVisualDepth);
        if (width <= 0 || height <= 0 || effectiveDepth <= 0)
            return;

        double alpha;
        if (area.GetStyleContext().LookupColor("window_fg_color", out var fg))
        {
            cr.SetSourceRgb(fg.Red, fg.Green, fg.Blue);
            alpha = 0.22;
        }
        else
        {
            cr.SetSourceRgb(0.5, 0.5, 0.5);
            alpha = 0.35;
        }

        cr.PushGroup();
        cr.LineWidth = 1.5;
        cr.LineCap = LineCap.Round;
        cr.LineJoin = LineJoin.Round;

        var ancestorLimit = Math.Min(_boundAncestorHasMoreSiblings.Count, effectiveDepth - 1);
        for (var level = 0; level < ancestorLimit; level++)
        {
            if (!_boundAncestorHasMoreSiblings[level])
                continue;

            var ancestorX = level == 0
                ? 28.0
                : FirstLevelIndent + (level - 1) * IndentPerDepth + IndentPerDepth / 2.0;
            cr.MoveTo(ancestorX, 0);
            cr.LineTo(ancestorX, height);
            cr.Stroke();
        }

        var parentX = effectiveDepth == 1
            ? 28.0
            : FirstLevelIndent + (effectiveDepth - 2) * IndentPerDepth + IndentPerDepth / 2.0;
        var startY = effectiveDepth == 1 && _boundIsFirstSibling ? 8.0 : 0.0;
        var branchY = Math.Min(26.0, height / 2.0);
        var radius = Math.Min(10.0, Math.Max(4.0, width - parentX - 6.0));

        cr.MoveTo(parentX, startY);
        cr.LineTo(parentX, Math.Max(startY, branchY - radius));
        cr.ArcNegative(parentX + radius, branchY - radius, radius, Math.PI, Math.PI / 2.0);
        cr.LineTo(Math.Max(parentX + radius, width - 4.0), branchY);
        cr.Stroke();

        if (!_boundIsLastSibling)
        {
            cr.MoveTo(parentX, Math.Max(startY, branchY - radius));
            cr.LineTo(parentX, height);
            cr.Stroke();
        }

        cr.PopGroupToSource();
        cr.PaintWithAlpha(alpha);
    }

    private void SetCommentText(string text, string? prebuiltMarkup)
    {
        try
        {
            comment_text_label.SetMarkup(prebuiltMarkup ?? RichLabelMarkup.Build(text));
        }
        catch (Exception)
        {
            comment_text_label.SetText(text);
        }
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }

    private static string FormatReplyCount(int replyCount)
    {
        return replyCount == 1 ? "1 reply" : $"{replyCount:N0} replies";
    }
}
