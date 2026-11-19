using YoutubeAPI.Models.Common;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
/// Selects appropriately sized thumbnail URLs for UI presentation, avoiding excessive bandwidth and memory consumption.
/// </summary>
public static class ThumbnailResolutionSelector
{
    // A video card in SilverScreen is 310 px wide by 189 px tall.
    // For HiDPI (1.5x-2x) displays, crisp visuals without excessive memory or bandwidth consumption require 480px-640px.
    private const int AcceptableMinCardWidth = 320;
    private const int PreferredMinCardWidth = 480;
    private const int PreferredMaxCardWidth = 640;

    /// <summary>
    /// Selects a thumbnail that matches the video card display size rather than blindly picking the 1080p/4k variant.
    /// Prefers crisp variants in the ~480px-640px range for HiDPI sharpness, falling back gracefully to 320p or higher resolutions.
    /// </summary>
    public static string SelectCardThumbnail(IReadOnlyList<Thumbnail> thumbnails)
    {
        if (thumbnails == null || thumbnails.Count == 0) return string.Empty;

        // 1. Prioritize candidates within the preferred crisp range [480px .. 640px] (e.g. 480p hqdefault, 640p sddefault).
        // Pick the smallest within this range to avoid downloading larger than necessary while preserving sharpness.
        Thumbnail? best = null;
        foreach (var t in thumbnails)
        {
            if (t.Width >= PreferredMinCardWidth && t.Width <= PreferredMaxCardWidth)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        // 2. If none in [480..640], fall back to candidates in [320..480) (e.g. 320p mqdefault).
        // Prefer the highest resolution within this lower range to get as close to preferred sharpness as possible.
        foreach (var t in thumbnails)
        {
            if (t.Width >= AcceptableMinCardWidth && t.Width < PreferredMinCardWidth)
            {
                if (best is null || t.Width > best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        // 3. If none in [320..640], find the closest thumbnail with width >= 320 (e.g. 720p instead of 1080p).
        foreach (var t in thumbnails)
        {
            if (t.Width >= AcceptableMinCardWidth)
            {
                if (best is null || t.Width < best.Width)
                    best = t;
            }
        }

        if (best is not null) return best.Url.ToString();

        // 4. Fallback: pick largest available if all are under 320px
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
