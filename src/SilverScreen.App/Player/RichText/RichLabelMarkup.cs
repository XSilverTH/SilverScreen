using System.Text;
using SilverScreen.Core.Common;

namespace SilverScreen.Player.RichText;

/// <summary>
///     Turns parsed rich-text segments into Pango markup for Gtk.Labels, so
///     timestamps, links, hashtags, and mentions render as clickable links.
/// </summary>
internal static class RichLabelMarkup
{
    public const string SeekScheme = "seek:";
    public const string HashtagScheme = "hashtag:";

    public static string Build(string? text)
    {
        return Build(YouTubeRichText.Parse(text));
    }

    public static string Build(IReadOnlyList<YouTubeRichTextSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.Link is null)
            {
                builder.Append(EscapeText(segment.Text));
                continue;
            }

            var href = segment.Link.Kind switch
            {
                YouTubeRichTextKind.Url => segment.Link.Value,
                YouTubeRichTextKind.Timestamp =>
                    $"{SeekScheme}{(long)segment.Link.Timestamp.GetValueOrDefault().TotalSeconds}",
                YouTubeRichTextKind.Hashtag => $"{HashtagScheme}{segment.Link.Value}",
                YouTubeRichTextKind.Mention => "https://www.youtube.com/@" + segment.Link.Value,
                _ => segment.Link.Value,
            };

            builder.Append("<a href=\"")
                .Append(EscapeText(href))
                .Append("\">")
                .Append(EscapeText(segment.Text))
                .Append("</a>");
        }

        return builder.ToString();
    }

    public static string EscapeText(string text)
    {
        return text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
