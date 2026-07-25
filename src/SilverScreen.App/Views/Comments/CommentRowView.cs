using Gtk;
using SilverScreen.Core.Models;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Comments;

public class CommentRowView : ViewBase<Box>
{
    private readonly Label _author;
    private readonly Label _likes;
    private readonly Label _publishedTime;
    private readonly Label _text;

    public CommentRowView()
    {
        _author = GetRequiredObject<Label>("comment_author_label");
        _publishedTime = GetRequiredObject<Label>("comment_published_time_label");
        _text = GetRequiredObject<Label>("comment_text_label");
        _likes = GetRequiredObject<Label>("comment_likes_label");
    }

    public void Bind(YouTubeComment comment)
    {
        _author.SetText(comment.AuthorName);
        _publishedTime.SetText(comment.PublishedTimeText);
        _publishedTime.SetVisible(!string.IsNullOrWhiteSpace(comment.PublishedTimeText));
        _text.SetText(comment.Text);
        _likes.SetText(FormatCount(comment.LikeCount));
    }

    public void Unbind()
    {
        _author.SetText(string.Empty);
        _publishedTime.SetText(string.Empty);
        _publishedTime.SetVisible(false);
        _text.SetText(string.Empty);
        _likes.SetText(string.Empty);
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }
}