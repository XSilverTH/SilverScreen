namespace SilverScreen.Core.Models;

public enum SessionCookieFormat
{
    NetscapeCookiesText,
}

public sealed record AccountSession(
    bool IsSignedIn,
    string? DisplayName = null,
    string? AvatarUrl = null,
    bool HasManualSession = false,
    SessionCookieFormat? CookieFormat = null)
{
    public static AccountSession SignedOut { get; } = new(false);
}
