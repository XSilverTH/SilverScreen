namespace SilverScreen.Core.Player;

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

    public static SponsorBlockCategoryColor GetColor(string category)
    {
        return category switch
        {
            Sponsor => new SponsorBlockCategoryColor("#00d400", 0, 212, 0, 0.7),
            SelfPromotion => new SponsorBlockCategoryColor("#ffff00", 255, 255, 0, 0.7),
            InteractionReminder => new SponsorBlockCategoryColor("#cc00ff", 204, 0, 255, 0.7),
            Intro => new SponsorBlockCategoryColor("#00ffff", 0, 255, 255, 0.7),
            Outro => new SponsorBlockCategoryColor("#0202ed", 2, 2, 237, 0.7),
            Preview => new SponsorBlockCategoryColor("#008fd6", 0, 143, 214, 0.7),
            Hook => new SponsorBlockCategoryColor("#395699", 57, 86, 153, 0.8),
            Filler => new SponsorBlockCategoryColor("#7300FF", 115, 0, 255, 0.9),
            _ => new SponsorBlockCategoryColor("#00d400", 0, 212, 0, 0.7)
        };
    }
}

public readonly record struct SponsorBlockCategoryColor(string Hex, byte Red, byte Green, byte Blue, double Opacity);

public sealed record SponsorBlockSegment(string Id, TimeSpan Start, TimeSpan End, string Category);