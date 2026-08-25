using Adw;
using Gtk;
using SilverScreen.Core.Player.Comments;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Comments;

public partial class CommentRowView(Action<string> repliesToggleRequested) : ViewBase<Bin>
{
    private readonly Action<string> _repliesToggleRequested =
        repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));

    private string? _boundCommentId;

    private void OnRepliesButtonClicked(object? sender, EventArgs args)
    {
        if (_boundCommentId is { } commentId)
            _repliesToggleRequested(commentId);
    }

    public void Bind(YouTubeComment comment, int replyCount, bool repliesVisible)
    {
        _boundCommentId = comment.Id;
        Widget.MarginStart = comment.ParentId is null ? 8 : 32;
        comment_author_label.SetText(comment.AuthorName);
        comment_published_time_label.SetText(comment.PublishedTimeText);
        comment_published_time_label.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
        comment_text_label.SetText(comment.Text);
        comment_likes_label.SetText(FormatCount(comment.LikeCount));
        comment_replies_button.SetVisible(replyCount > 0);
        comment_replies_button.SetLabel(repliesVisible
            ? $"Hide {FormatReplyCount(replyCount)}"
            : $"Show {FormatReplyCount(replyCount)}");
    }

    public void Unbind()
    {
        _boundCommentId = null;
        Widget.MarginStart = 8;
        comment_author_label.SetText(string.Empty);
        comment_published_time_label.SetText(string.Empty);
        comment_published_time_label.SetVisible(false);
        comment_text_label.SetText(string.Empty);
        comment_likes_label.SetText(string.Empty);
        comment_replies_button.SetVisible(false);
        comment_replies_button.SetLabel(string.Empty);
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