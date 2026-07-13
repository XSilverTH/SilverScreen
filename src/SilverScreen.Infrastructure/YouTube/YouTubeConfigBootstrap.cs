using System.Text.RegularExpressions;

namespace SilverScreen.Infrastructure.YouTube;

public sealed record YouTubeBootstrapConfig(string ApiKey, string ClientVersion, string? VisitorData)
{
    public override string ToString() =>
        $"YouTubeBootstrapConfig {{ ClientVersion = {ClientVersion}, Credentials = [REDACTED] }}";
}

public static class YouTubeConfigBootstrap
{
    private static readonly Regex ApiKeyRegex = new(
        """
        "(?:INNERTUBE_API_KEY|API_KEY|innertubeApiKey)"\s*:\s*"([^"]+)"
        """,
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ClientVersionRegex = new(
        """
        "(?:INNERTUBE_CONTEXT_CLIENT_VERSION|INNERTUBE_CLIENT_VERSION|clientVersion)"\s*:\s*"([^"]+)"
        """,
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex VisitorDataRegex = new(
        """
        "(?:VISITOR_DATA|visitorData)"\s*:\s*"([^"]+)"
        """,
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    public static YouTubeBootstrapConfig? Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var apiKeyMatch = ApiKeyRegex.Match(html);
        var clientVersionMatch = ClientVersionRegex.Match(html);
        var visitorDataMatch = VisitorDataRegex.Match(html);

        if (!apiKeyMatch.Success || !clientVersionMatch.Success)
            return null;

        var apiKey = apiKeyMatch.Groups[1].Value;
        var clientVersion = clientVersionMatch.Groups[1].Value;
        var visitorData = visitorDataMatch.Success ? visitorDataMatch.Groups[1].Value : null;

        return new YouTubeBootstrapConfig(apiKey, clientVersion, visitorData);
    }
}