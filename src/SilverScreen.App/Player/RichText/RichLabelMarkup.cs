using System.Buffers;
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
    private static readonly SearchValues<char> XmlEscapeChars = SearchValues.Create("&<>\"'");

    public static string Build(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Build(YouTubeRichText.Parse(text));
    }

    private static string Build(IReadOnlyList<YouTubeRichTextSegment> segments)
    {
        if (segments.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.Link is null)
            {
                AppendEscapedText(builder, segment.Text);
                continue;
            }

            var href = segment.Link.Kind switch
            {
                YouTubeRichTextKind.Url => segment.Link.Value,
                YouTubeRichTextKind.Timestamp =>
                    $"{SeekScheme}{(long)segment.Link.Timestamp.GetValueOrDefault().TotalSeconds}",
                YouTubeRichTextKind.Hashtag => $"{HashtagScheme}{segment.Link.Value}",
                YouTubeRichTextKind.Mention => "https://www.youtube.com/@" + segment.Link.Value,
                _ => segment.Link.Value
            };

            builder.Append("<a href=\"");
            AppendEscapedText(builder, href);
            builder.Append("\">");
            AppendEscapedText(builder, segment.Text);
            builder.Append("</a>");
        }

        return builder.ToString();
    }

    private static void AppendEscapedText(StringBuilder builder, string text)
    {
        var span = text.AsSpan();
        while (!span.IsEmpty)
        {
            var nextSpecial = span.IndexOfAny(XmlEscapeChars);
            if (nextSpecial < 0)
            {
                builder.Append(span);
                return;
            }

            if (nextSpecial > 0)
                builder.Append(span[..nextSpecial]);

            builder.Append(span[nextSpecial] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => string.Empty
            });

            span = span[(nextSpecial + 1)..];
        }
    }
}