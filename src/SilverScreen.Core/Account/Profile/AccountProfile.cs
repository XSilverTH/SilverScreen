namespace SilverScreen.Core.Account.Profile;

public sealed record AccountProfile(
    string DisplayName,
    string? AvatarUrl = null);