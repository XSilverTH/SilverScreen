using Adw;
using Gtk;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentRowView(Action<string> repliesToggleRequested, Func<string, bool> linkActivated)
    : ViewBase<Bin>
{
    private readonly Func<string, bool> _linkActivated =
        linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));

    private readonly Action<string> _repliesToggleRequested =
        repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));

    private bool? _boundRepliesVisible;
    private int _boundReplyCount = -1;
    private string? _boundText;

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

    public void Bind(YouTubeComment comment, int replyCount, bool repliesVisible)
    {
        Bind(comment, replyCount, repliesVisible, null);
    }

    internal void Bind(YouTubeComment comment, int replyCount, bool repliesVisible, string? prebuiltMarkup)
    {
        var sameContent = string.Equals(BoundCommentId, comment.Id, StringComparison.Ordinal) &&
                          string.Equals(_boundText, comment.Text, StringComparison.Ordinal);

        BoundCommentId = comment.Id;
        if (!sameContent)
        {
            _boundText = comment.Text;
            Widget.MarginStart = comment.ParentId is null ? 8 : 32;
            comment_author_label.SetText(comment.AuthorName);
            comment_published_time_label.SetText(comment.PublishedTimeText);
            comment_published_time_label.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
            SetCommentText(comment.Text, prebuiltMarkup);
            comment_likes_label.SetText(FormatCount(comment.LikeCount));
        }

        if (!sameContent || _boundReplyCount != replyCount || _boundRepliesVisible != repliesVisible)
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
        Widget.MarginStart = 8;
        comment_author_label.SetText(string.Empty);
        comment_published_time_label.SetText(string.Empty);
        comment_published_time_label.SetVisible(false);
        comment_text_label.SetText(string.Empty);
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