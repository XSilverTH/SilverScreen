using System.Text.RegularExpressions;

namespace SilverScreen.Core.Common;

public enum YouTubeRichTextKind
{
    Url,
    Timestamp,
    Hashtag,
    Mention,
}

/// <summary>
///     A linked span found in video descriptions or comments.
///     <see cref="Text" /> is the original display text; <see cref="Value" /> is the
///     normalized payload (absolute URL, tag without '#', handle without '@').
///     <see cref="Timestamp" /> is set for timestamp links.
/// </summary>
public sealed record YouTubeRichTextToken(
    YouTubeRichTextKind Kind,
    string Text,
    string Value,
    TimeSpan? Timestamp = null);

/// <summary>
///     A run of source text: plain when <see cref="Link" /> is null, clickable otherwise.
/// </summary>
public sealed record YouTubeRichTextSegment(string Text, YouTubeRichTextToken? Link);

/// <summary>
///     Finds clickable spans (URLs, timestamps, hashtags, @mentions) in YouTube
///     descriptions and comments. Pure text processing; presentation layers turn
///     the segments into markup.
/// </summary>
public static partial class YouTubeRichText
{
    private static readonly char[] UrlTrailingPunctuation = ['.', ',', ';', ':', '!', '?', '\'', '"', '*'];

    public static IReadOnlyList<YouTubeRichTextSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var links = new List<(int Start, int Length, YouTubeRichTextToken Token)>();

        foreach (Match match in UrlPattern().Matches(text))
        {
            var url = TrimUrlEnd(match.Value);
            if (url.Length == 0)
                continue;

            var start = match.Index;
            var token = new YouTubeRichTextToken(
                YouTubeRichTextKind.Url,
                url,
                NormalizeUrl(url));
            links.Add((start, url.Length, token));
        }

        var urlSpans = links.Select(link => (link.Start, End: link.Start + link.Length)).ToArray();

        foreach (Match match in TimestampPattern().Matches(text))
        {
            if (!TryParseTimestamp(match.ValueSpan, out var timestamp))
                continue;

            AddIfOutsideUrls(links, urlSpans, match.Index, match.Length,
                new YouTubeRichTextToken(YouTubeRichTextKind.Timestamp, match.Value, match.Value, timestamp));
        }

        foreach (Match match in HashtagPattern().Matches(text))
        {
            var tag = match.Groups[1].Value;
            if (!tag.Any(char.IsLetter))
                continue;

            AddIfOutsideUrls(links, urlSpans, match.Index, match.Length,
                new YouTubeRichTextToken(YouTubeRichTextKind.Hashtag, match.Value, tag));
        }

        foreach (Match match in MentionPattern().Matches(text))
        {
            var handle = match.Groups[1].Value.TrimEnd('.', '-', '_');
            if (handle.Length < 2)
                continue;

            var trimmed = "@" + handle;
            AddIfOutsideUrls(links, urlSpans, match.Index, trimmed.Length,
                new YouTubeRichTextToken(YouTubeRichTextKind.Mention, trimmed, handle));
        }

        if (links.Count == 0)
            return [new YouTubeRichTextSegment(text, null)];

        links.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        var segments = new List<YouTubeRichTextSegment>(links.Count * 2 + 1);
        var position = 0;
        foreach (var (start, length, token) in links)
        {
            if (start < position)
                continue;
            if (start > position)
                segments.Add(new YouTubeRichTextSegment(text[position..start], null));
            segments.Add(new YouTubeRichTextSegment(token.Text, token));
            position = start + length;
        }

        if (position < text.Length)
            segments.Add(new YouTubeRichTextSegment(text[position..], null));

        return segments;
    }

    /// <summary>
    ///     Parses "m:ss" or "h:mm:ss" timestamps. Minutes may exceed 59 without hours
    ///     (YouTube links "75:30"); with hours, minutes must be 00-59. Seconds are
    ///     always two digits 00-59.
    /// </summary>
    public static bool TryParseTimestamp(ReadOnlySpan<char> text, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        var firstColon = text.IndexOf(':');
        if (firstColon <= 0 || firstColon == text.Length - 1)
            return false;

        var secondColon = text[(firstColon + 1)..].IndexOf(':');
        int hours;
        ReadOnlySpan<char> minutesSpan;
        ReadOnlySpan<char> secondsSpan;
        if (secondColon < 0)
        {
            hours = 0;
            minutesSpan = text[..firstColon];
            secondsSpan = text[(firstColon + 1)..];
            if (minutesSpan.Length is < 1 or > 3 || !IsAllDigits(minutesSpan))
                return false;
        }
        else
        {
            var hoursSpan = text[..firstColon];
            minutesSpan = text.Slice(firstColon + 1, secondColon);
            secondsSpan = text[(firstColon + 1 + secondColon + 1)..];
            if (hoursSpan.Length == 0 || minutesSpan.Length != 2 || !IsAllDigits(hoursSpan) ||
                !IsAllDigits(minutesSpan))
                return false;
            if (!int.TryParse(hoursSpan, out hours))
                return false;
            if (int.Parse(minutesSpan) > 59)
                return false;
        }

        if (secondsSpan.Length != 2 || !IsAllDigits(secondsSpan))
            return false;

        if (!int.TryParse(minutesSpan, out var minutes) || !int.TryParse(secondsSpan, out var seconds))
            return false;
        if (seconds > 59)
            return false;

        try
        {
            timestamp = new TimeSpan(0, hours, minutes, seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return true;
    }

    private static void AddIfOutsideUrls(
        List<(int Start, int Length, YouTubeRichTextToken Token)> links,
        (int Start, int End)[] urlSpans,
        int start,
        int length,
        YouTubeRichTextToken token)
    {
        var end = start + length;
        foreach (var (urlStart, urlEnd) in urlSpans)
        {
            if (start < urlEnd && end > urlStart)
                return;
        }

        links.Add((start, length, token));
    }

    private static string TrimUrlEnd(string url)
    {
        var end = url.Length;
        while (end > 0 && (char.IsWhiteSpace(url[end - 1]) || Array.IndexOf(UrlTrailingPunctuation, url[end - 1]) >= 0))
            end--;

        var openParentheses = 0;
        var closeParentheses = 0;
        for (var i = 0; i < end; i++)
        {
            if (url[i] == '(') openParentheses++;
            else if (url[i] == ')') closeParentheses++;
        }

        while (end > 0 && url[end - 1] == ')' && closeParentheses > openParentheses)
        {
            end--;
            closeParentheses--;
        }

        return url[..end];
    }

    private static string NormalizeUrl(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        return "https://" + url;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return false;

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }

    [GeneratedRegex(
        @"(?:https?://|www\.|(?<![\p{L}\p{N}_.=\-/?&])(?:(?:m|music)\.)?youtube\.com/|(?<![\p{L}\p{N}_.=\-/?&])youtu\.be/)[^\s<>\[\]{}|\\^`]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(
        @"(?<![\d:])(?:(\d+):)?(\d{1,3}):([0-5]\d)(?![\d:])",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_&#;])#([\p{L}\p{N}_]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex HashtagPattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_.+\-])@([\p{L}\p{N}][\p{L}\p{N}._\-]{0,29})",
        RegexOptions.CultureInvariant)]
    private static partial Regex MentionPattern();
}
