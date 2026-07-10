using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YouTubeCredentials
{
    public string CookieHeader { get; }
    public string Sapisid { get; }

    private YouTubeCredentials(string cookieHeader, string sapisid)
    {
        CookieHeader = cookieHeader;
        Sapisid = sapisid;
    }

    public static YouTubeCredentials? ParseNetscape(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var cookies = new List<(string Name, string Value)>();
        string? sapisid = null;
        string? secureSapisid = null;

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            {
                continue;
            }

            var parts = trimmed.Split('\t');
            if (parts.Length < 7)
            {
                continue;
            }

            var domain = parts[0].Trim().ToLowerInvariant();
            var name = parts[5].Trim();
            var value = parts[6].Trim();

            // We only care about youtube.com or its subdomains
            if (domain == "youtube.com" || domain == ".youtube.com" || domain.EndsWith(".youtube.com"))
            {
                cookies.Add((name, value));

                if (name == "SAPISID")
                {
                    sapisid = value;
                }
                else if (name == "__Secure-3PAPISID")
                {
                    secureSapisid = value;
                }
            }
        }

        // Prioritize secure sapisid if available
        var activeSapisid = secureSapisid ?? sapisid;
        if (string.IsNullOrEmpty(activeSapisid))
        {
            return null; // Missing critical authentication cookie
        }

        // Build Cookie header
        var sb = new StringBuilder();
        foreach (var cookie in cookies)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(cookie.Name).Append('=').Append(cookie.Value);
        }

        return new YouTubeCredentials(sb.ToString(), activeSapisid);
    }

    public string GenerateSapisidHash(long timestamp)
    {
        // derive SAPISIDHASH using SHA-1 of '{unixTimestamp} {SAPISID} https://www.youtube.com'
        var data = $"{timestamp} {Sapisid} https://www.youtube.com";
        byte[] hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hashBytes);
    }

    public override string ToString()
    {
        return "YouTubeCredentials [REDACTED]";
    }
}
