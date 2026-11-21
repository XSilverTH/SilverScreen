namespace SilverScreen.Browsing.Channel;

internal static class ChannelFormatter
{
    public static string? ExtractHandle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var atIndex = url.IndexOf('@');
        if (atIndex < 0) return null;
        var handle = url[atIndex..].TrimEnd('/');
        var slashIndex = handle.IndexOf('/');
        return slashIndex > 0 ? handle[..slashIndex] : handle;
    }

    public static string FormatSubscribers(long count)
    {
        return count switch
        {
            >= 1_000_000 => $"{count / 1_000_000.0:0.#}M subscribers",
            >= 1_000 => $"{count / 1_000.0:0.#}K subscribers",
            _ => count == 1 ? "1 subscriber" : $"{count:N0} subscribers"
        };
    }

    public static string? ExtractContactInfo(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var lines = description.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contactLines = lines.Where(line => line.Contains("contact", StringComparison.OrdinalIgnoreCase) ||
                                               line.Contains("business", StringComparison.OrdinalIgnoreCase) ||
                                               line.Contains("inquir", StringComparison.OrdinalIgnoreCase) ||
                                               line.Contains("email", StringComparison.OrdinalIgnoreCase) ||
                                               (line.Contains('@') &&
                                                (line.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
                                                 line.Contains(".org", StringComparison.OrdinalIgnoreCase) ||
                                                 line.Contains(".net", StringComparison.OrdinalIgnoreCase)))).ToList();

        return contactLines.Count > 0 ? string.Join("\n", contactLines) : null;
    }
}
