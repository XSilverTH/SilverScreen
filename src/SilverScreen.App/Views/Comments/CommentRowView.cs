using Gtk;
using SilverScreen.Core.Models;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Comments;

public partial class CommentRowView : ViewBase<Box>
{
    private readonly Label _author;
    private readonly Label _likes;
    private readonly Label _publishedTime;
    private readonly Button _repliesButton;
    private readonly Action<string> _repliesToggleRequested;
    private readonly Label _text;
    private string? _boundCommentId;

    public CommentRowView(Action<string> repliesToggleRequested)
    {
        _repliesToggleRequested =
            repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));
        _author = GetRequiredObject<Label>("comment_author_label");
        _publishedTime = GetRequiredObject<Label>("comment_published_time_label");
        _text = GetRequiredObject<Label>("comment_text_label");
        _likes = GetRequiredObject<Label>("comment_likes_label");
        _repliesButton = GetRequiredObject<Button>("comment_replies_button");
    }

    private void OnRepliesButtonClicked(object? sender, EventArgs args)
    {
        if (_boundCommentId is { } commentId)
            _repliesToggleRequested(commentId);
    }

    public void Bind(YouTubeComment comment, int replyCount, bool repliesVisible)
    {
        _boundCommentId = comment.Id;
        Widget.MarginStart = comment.ParentId is null ? 8 : 32;
        _author.SetText(comment.AuthorName);
        _publishedTime.SetText(comment.PublishedTimeText);
        _publishedTime.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
        _text.SetText(comment.Text);
        _likes.SetText(FormatCount(comment.LikeCount));
        _repliesButton.SetVisible(replyCount > 0);
        _repliesButton.SetLabel(repliesVisible
            ? $"Hide {FormatReplyCount(replyCount)}"
            : $"Show {FormatReplyCount(replyCount)}");
    }

    public void Unbind()
    {
        _boundCommentId = null;
        Widget.MarginStart = 8;
        _author.SetText(string.Empty);
        _publishedTime.SetText(string.Empty);
        _publishedTime.SetVisible(false);
        _text.SetText(string.Empty);
        _likes.SetText(string.Empty);
        _repliesButton.SetVisible(false);
        _repliesButton.SetLabel(string.Empty);
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