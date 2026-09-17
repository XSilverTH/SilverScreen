using System.Text.RegularExpressions;

namespace SilverScreen.Core.Common;

/// <summary>Removes credential material from diagnostics before they reach logs or user-facing surfaces.</summary>
public static partial class DiagnosticSanitizer
{
    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        message = SapisidHashRegex().Replace(message, "SAPISIDHASH [REDACTED]");
        message = AuthorizationHeaderRegex().Replace(message, "$1[REDACTED]");
        message = CookieHeaderRegex().Replace(message, "$1[REDACTED]");
        message = BearerRegex().Replace(message, "$1 [REDACTED]");
        message = CookieAssignmentRegex().Replace(message, "$1[REDACTED]");
        message = QuerySecretRegex().Replace(message, "$1[REDACTED]");
        message = JsonSecretRegex().Replace(message, "$1[REDACTED]$2");
        return ArgumentSecretRegex().Replace(message, "$1[REDACTED]");
    }

    [GeneratedRegex(@"SAPISIDHASH(?:\s+|\s*[=:]\s*)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SapisidHashRegex();

    [GeneratedRegex(@"(Authorization\s*:\s*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(Cookie\s*:\s*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CookieHeaderRegex();

    [GeneratedRegex(@"\b(Bearer|Basic)\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\b((?:SAPISID|__Secure-3PAPISID|__Secure-1PAPISID|SID|SSID|HSID|APISID|LOGIN_INFO|__Secure-1PSID|__Secure-3PSID)\s*[=:]\s*)[^;\s&,]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CookieAssignmentRegex();

[GeneratedRegex(@"\b((?:access_token|refresh_token|id_token|token|poToken|continuation|params|key|api_key|apikey)\s*[=:]\s*)[^&\s,;}""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(@"(""(?:access_token|refresh_token|id_token|token|poToken|continuation|params|key|api_key|apikey)""\s*:\s*"")[^""]*("")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretRegex();

[GeneratedRegex(@"(--(?:password|client-secret|access-token|refresh-token|token|api-key|cookies(?:-from-browser)?)(?:=|\s+))(?:""[^""]*""|'[^']*'|\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentSecretRegex();
}
