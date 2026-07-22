namespace SilverScreen.Infrastructure.YouTube;

public sealed class YouTubeHomeClientOptions
{
    public static string Origin => "https://www.youtube.com";
    public static string Referer => "https://www.youtube.com/";

    public static string UserAgent =>
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public int? AuthUser { get; set; }
}