namespace SilverScreen.Core.Models;

public sealed record AccountSession(bool IsSignedIn, string? DisplayName = null, string? AvatarUrl = null)
{
    public static AccountSession SignedOut { get; } = new(false);
}
