using YoutubeAPI.Models.Common;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
/// Selects appropriately sized thumbnail URLs for UI presentation, avoiding excessive bandwidth and memory consumption.
/// </summary>
public static class ThumbnailResolutionSelector
{
    // A video card in SilverScreen is 310 px wide by 189 px tall.
    // For 1x-2x display scale, optimal width is between 320 and 640 px.
    private const int IdealMinCardWidth = 320;
    private const int IdealMaxCardWidth = 640;

    /// <summary>
    /// Selects a thumbnail that matches the video card display size rather than blindly picking the 1080p/4k variant.
    /// </summary>
    public static string SelectCardThumbnail(IReadOnlyList<Thumbnail> thumbnails)
    {
        if (thumbnails == null || thumbnails.Count == 0) return string.Empty;

        // 1. Look for thumbnails within optimal card resolution [320px .. 640px]
        Thumbnail? best = null;
        foreach (var t in thumbnails)
        {
            if (t.Width >= IdealMinCardWidth && t.Width <= IdealMaxCardWidth)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        // 2. If none in [320..640], find the closest thumbnail with width >= 320 (e.g. 720p instead of 1080p)
        foreach (var t in thumbnails)
        {
            if (t.Width >= IdealMinCardWidth)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        // 3. Fallback: pick largest available if all are under 320px
        return thumbnails.OrderBy(t => (long)t.Width * t.Height).LastOrDefault()?.Url.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Selects an avatar thumbnail appropriate for small circular badges (56-80px).
    /// </summary>
    public static string? SelectAvatarThumbnail(IReadOnlyList<Thumbnail> thumbnails)
    {
        if (thumbnails == null || thumbnails.Count == 0) return null;

        Thumbnail? best = null;
        foreach (var t in thumbnails)
        {
            if (t.Width >= 80 && t.Width <= 200)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        foreach (var t in thumbnails)
        {
            if (t.Width >= 80)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        return thumbnails.FirstOrDefault()?.Url.ToString();
    }
}
