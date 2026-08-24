using System.Globalization;
using System.Net;

namespace SilverScreen.Core.Account.Session;

public static class NetscapeCookieParser
{
    public static CookieContainer? CreateCookieContainer(string? netscapeContent)
    {
        if (string.IsNullOrWhiteSpace(netscapeContent))
            return null;

        var container = new CookieContainer();
        var lines = netscapeContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Trim();
            var httpOnly = line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase);
            if (line.StartsWith('#') && !httpOnly) continue;
            if (httpOnly) line = line["#HttpOnly_".Length..].Trim();

            var fields = line.Split('\t');
            if (fields.Length < 7) continue;

            var domain = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(fields[5])) continue;

            try
            {
                var name = fields[5].Trim();
                var value = fields[6].Trim();
                var path = string.IsNullOrWhiteSpace(fields[2]) ? "/" : fields[2].Trim();
                var secure = bool.TryParse(fields[3].Trim(), out var s) && s;

                var cookie = new Cookie(name, value, path, domain)
                {
                    Secure = secure,
                    HttpOnly = httpOnly
                };

                if (long.TryParse(fields[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var expiration) &&
                    expiration > 0) cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(expiration).UtcDateTime;

                container.Add(cookie);
            }
            catch (CookieException)
            {
                // Ignore cookies outside CookieContainer's accepted domain syntax from browser exports
            }
            catch (ArgumentException)
            {
            }
        }

        return container;
    }
}