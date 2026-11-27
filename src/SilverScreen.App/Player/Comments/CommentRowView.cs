using Adw;
using Gtk;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentRowView(
    Action<string> repliesToggleRequested,
    Func<string, bool> linkActivated,
    Action<string>? translateRequested = null)
    : ViewBase<Bin>
{
    private readonly Func<string, bool> _linkActivated =
        linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));

    private readonly Action<string> _repliesToggleRequested =
        repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));

    private readonly Action<string>? _translateRequested = translateRequested;

    private bool? _boundCanTranslate;
    private bool? _boundIsTranslated;
    private bool? _boundIsTranslating;
    private bool? _boundRepliesVisible;
    private int _boundReplyCount = -1;
    private string? _boundText;
    private string? _boundTranslationError;

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
        string? translationError = null)
    {
        var displayText = isTranslated && !string.IsNullOrEmpty(translatedText)
            ? translatedText
            : comment.Text;
        var sameId = string.Equals(BoundCommentId, comment.Id, StringComparison.Ordinal);
        var sameText = string.Equals(_boundText, displayText, StringComparison.Ordinal);

        BoundCommentId = comment.Id;
        if (!sameId)
        {
            Widget.MarginStart = comment.ParentId is null ? 8 : 32;
            comment_author_label.SetText(comment.AuthorName);
            comment_published_time_label.SetText(comment.PublishedTimeText);
            comment_published_time_label.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
            comment_likes_label.SetText(FormatCount(comment.LikeCount));
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

        if (!sameId || _boundReplyCount != replyCount || _boundRepliesVisible != repliesVisible)
        {
            _boundReplyCount = replyCount;
            _boundRepliesVisible = repliesVisible;
            comment_replies_button.SetVisible(replyCount > 0);
            if (replyCount > 0)
                comment_replies_button.SetLabel(repliesVisible
                    ? $"Hide {FormatReplyCount(replyCount)}"
                    : $"Show {FormatReplyCount(replyCount)}");
            else
                comment_replies_button.SetLabel(string.Empty);
        }
    }

    public void Unbind()
    {
        BoundCommentId = null;
        _boundText = null;
        _boundReplyCount = -1;
        _boundRepliesVisible = null;
        _boundCanTranslate = null;
        _boundIsTranslated = null;
        _boundIsTranslating = null;
        _boundTranslationError = null;
        Widget.MarginStart = 8;
        comment_author_label.SetText(string.Empty);
        comment_published_time_label.SetText(string.Empty);
        comment_published_time_label.SetVisible(false);
        comment_text_label.SetText(string.Empty);
        comment_translate_button.SetVisible(false);
        comment_translate_button.SetSensitive(true);
        comment_translate_button.RemoveCssClass("accent");
        comment_likes_label.SetText(string.Empty);
        comment_replies_button.SetVisible(false);
        comment_replies_button.SetLabel(string.Empty);
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