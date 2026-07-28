namespace SilverScreen.Core.Models;

public static class SponsorBlockCategories
{
    public const string Sponsor = "sponsor";
    public const string SelfPromotion = "selfpromo";
    public const string InteractionReminder = "interaction";
    public const string Intro = "intro";
    public const string Outro = "outro";
    public const string Preview = "preview";
    public const string Hook = "hook";
    public const string Filler = "filler";

    public static readonly string[] All =
    [
        Sponsor,
        SelfPromotion,
        InteractionReminder,
        Intro,
        Outro,
        Preview,
        Hook,
        Filler
    ];
}

public sealed record SponsorBlockSegment(string Id, TimeSpan Start, TimeSpan End, string Category);
