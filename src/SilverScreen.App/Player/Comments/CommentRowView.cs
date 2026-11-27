using Cairo;
using Gtk;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentRowView : ViewBase<Box>
{
    private const int IndentPerDepth = 24;
    private const int MaxVisualDepth = 4;

    private readonly Func<string, bool> _linkActivated;
    private readonly Action<string>? _loadMoreRepliesRequested;
    private readonly Action<string> _repliesToggleRequested;
    private readonly Action<string>? _translateRequested;

    private IReadOnlyList<bool> _boundAncestorHasMoreSiblings = [];
    private bool? _boundCanTranslate;
    private int _boundDepth;
    private bool? _boundHasMoreReplies;
    private bool _boundIsLastSibling = true;
    private bool? _boundIsLoadingReplies;
    private bool? _boundIsTranslated;
    private bool? _boundIsTranslating;
    private string? _boundLoadMoreRepliesParentId;
    private bool? _boundRepliesVisible;
    private int _boundReplyCount = -1;
    private string? _boundText;
    private string? _boundTranslationError;

    public CommentRowView(
        Action<string> repliesToggleRequested,
        Func<string, bool> linkActivated,
        Action<string>? translateRequested = null,
        Action<string>? loadMoreRepliesRequested = null)
    {
        _repliesToggleRequested =
            repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));
        _linkActivated = linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));
        _translateRequested = translateRequested;
        _loadMoreRepliesRequested = loadMoreRepliesRequested;

        comment_tree_gutter.SetDrawFunc(DrawTreeLines);
    }

    internal string? BoundCommentId { get; private set; }

    private bool OnCommentTextActivateLink(Label sender, Label.ActivateLinkSignalArgs args)
    {
        return _linkActivated(args.Uri);
    }

    private void OnRepliesButtonClicked(object? sender, EventArgs args)
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
        bool isLastSibling = true,
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
            comment_published_time_label.SetText(comment.PublishedTimeText);
            comment_published_time_label.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
            comment_likes_label.SetText(FormatCount(comment.LikeCount));
        }

        _boundDepth = Math.Max(0, depth);
        _boundIsLastSibling = isLastSibling;
        _boundAncestorHasMoreSiblings = ancestorHasMoreSiblings ?? [];
        var effectiveDepth = Math.Min(_boundDepth, MaxVisualDepth);
        comment_tree_gutter.SetVisible(effectiveDepth > 0);
        if (effectiveDepth > 0)
        {
            comment_tree_gutter.SetContentWidth(effectiveDepth * IndentPerDepth);
            comment_tree_gutter.QueueDraw();
        }

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

        if (!sameId ||
            _boundReplyCount != replyCount ||
            _boundRepliesVisible != repliesVisible ||
            _boundHasMoreReplies != hasMoreReplies ||
            _boundIsLoadingReplies != isLoadingReplies)
        {
            _boundReplyCount = replyCount;
            _boundRepliesVisible = repliesVisible;
            _boundHasMoreReplies = hasMoreReplies;
            _boundIsLoadingReplies = isLoadingReplies;

            var showRepliesToggle = replyCount > 0 || hasMoreReplies;
            comment_replies_button.SetVisible(showRepliesToggle);
            comment_replies_button.SetSensitive(!isLoadingReplies);
            if (showRepliesToggle)
            {
                comment_replies_chevron.SetFromIconName(repliesVisible
                    ? "pan-up-symbolic"
                    : "pan-down-symbolic");
                comment_replies_label.SetText(isLoadingReplies
                    ? "Loading replies…"
                    : repliesVisible
                        ? "Hide replies"
                        : replyCount > 0
                            ? $"Show {FormatReplyCount(replyCount)}"
                            : "Show replies");
            }
            else
            {
                comment_replies_label.SetText(string.Empty);
            }
        }

        if (!sameId || !string.Equals(_boundLoadMoreRepliesParentId, loadMoreRepliesParentId, StringComparison.Ordinal))
        {
            _boundLoadMoreRepliesParentId = loadMoreRepliesParentId;
            comment_load_more_replies_button.SetVisible(!string.IsNullOrEmpty(loadMoreRepliesParentId));
        }
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
        _boundCanTranslate = null;
        _boundIsTranslated = null;
        _boundIsTranslating = null;
        _boundTranslationError = null;
        _boundDepth = 0;
        _boundIsLastSibling = true;
        _boundAncestorHasMoreSiblings = [];
        comment_tree_gutter.SetVisible(false);
        comment_author_label.SetText(string.Empty);
        comment_published_time_label.SetText(string.Empty);
        comment_published_time_label.SetVisible(false);
        comment_text_label.SetText(string.Empty);
        comment_translate_button.SetVisible(false);
        comment_translate_button.SetSensitive(true);
        comment_translate_button.RemoveCssClass("accent");
        comment_likes_label.SetText(string.Empty);
        comment_replies_button.SetVisible(false);
        comment_replies_button.SetSensitive(true);
        comment_replies_label.SetText(string.Empty);
        comment_load_more_replies_button.SetVisible(false);
    }

    private void DrawTreeLines(DrawingArea area, Context cr, int width, int height)
    {
        var effectiveDepth = Math.Min(_boundDepth, MaxVisualDepth);
        if (width <= 0 || height <= 0 || effectiveDepth <= 0)
            return;

        if (area.GetStyleContext().LookupColor("window_fg_color", out var fg))
            cr.SetSourceRgba(fg.Red, fg.Green, fg.Blue, 0.22);
        else
            cr.SetSourceRgba(0.5, 0.5, 0.5, 0.35);

        cr.LineWidth = 1.5;
        cr.LineCap = LineCap.Round;
        cr.LineJoin = LineJoin.Round;

        var ancestorLimit = Math.Min(_boundAncestorHasMoreSiblings.Count, effectiveDepth - 1);
        for (var level = 0; level < ancestorLimit; level++)
        {
            if (!_boundAncestorHasMoreSiblings[level])
                continue;

            var ancestorX = level * IndentPerDepth + IndentPerDepth / 2.0;
            cr.MoveTo(ancestorX, 0);
            cr.LineTo(ancestorX, height);
            cr.Stroke();
        }

        var parentX = (effectiveDepth - 1) * IndentPerDepth + IndentPerDepth / 2.0;
        var branchY = Math.Min(31.0, height / 2.0);
        var radius = Math.Min(10.0, IndentPerDepth / 2.0);

        cr.MoveTo(parentX, 0);
        cr.LineTo(parentX, Math.Max(0, branchY - radius));
        cr.ArcNegative(parentX + radius, branchY - radius, radius, Math.PI, Math.PI / 2.0);
        cr.LineTo(width, branchY);
        cr.Stroke();

        if (!_boundIsLastSibling)
        {
            cr.MoveTo(parentX, Math.Max(0, branchY - radius));
            cr.LineTo(parentX, height);
            cr.Stroke();
        }
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