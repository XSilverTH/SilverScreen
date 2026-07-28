namespace SilverScreen.Core.Models;

public sealed record AccountProfile(
    string DisplayName,
    string? AvatarUrl = null);