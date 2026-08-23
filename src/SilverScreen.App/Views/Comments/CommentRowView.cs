using Gtk;
using SilverScreen.Core.Models;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Comments;

public partial class CommentRowView : ViewBase<Box>
{
    [BlueprintWidget("comment_author_label")]
    private Label _author = null!;

    [BlueprintWidget("comment_likes_label")]
    private Label _likes = null!;

    [BlueprintWidget("comment_published_time_label")]
    private Label _publishedTime = null!;

    [BlueprintWidget("comment_replies_button")]
    private Button _repliesButton = null!;

    [BlueprintWidget("comment_text_label")]
    private Label _text = null!;

    private readonly Action<string> _repliesToggleRequested;
    private string? _boundCommentId;

    public CommentRowView(Action<string> repliesToggleRequested)
    {
        _repliesToggleRequested =
            repliesToggleRequested ?? throw new ArgumentNullException(nameof(repliesToggleRequested));
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