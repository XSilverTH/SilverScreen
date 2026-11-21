using System.Globalization;
using SilverScreen.Core.Domain;

namespace SilverScreen.Browsing.Components;

internal static class VideoCardFormatting
{
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";

    public static string FormatUploadAge(DateOnly uploadDate, DateOnly today)
    {
        var elapsedDays = Math.Max(0, today.DayNumber - uploadDate.DayNumber);
        return elapsedDays switch
        {
            0 => "Today",
            1 => "1 day ago",
            < 7 => $"{elapsedDays} days ago",
            < 30 => FormatWholeUnits(elapsedDays / 7, "week"),
            < 365 => FormatWholeUnits(elapsedDays / 30, "month"),
            _ => FormatWholeUnits(elapsedDays / 365, "year")
        };
    }

    public static string FormatUploadAge(DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var elapsed = now - publishedAt;
        if (elapsed <= TimeSpan.Zero || elapsed < TimeSpan.FromMinutes(1)) return "Just now";
        if (elapsed < TimeSpan.FromHours(1)) return FormatWholeUnits((int)elapsed.TotalMinutes, "minute");
        if (elapsed < TimeSpan.FromDays(1)) return FormatWholeUnits((int)elapsed.TotalHours, "hour");
        return FormatUploadAge(DateOnly.FromDateTime(publishedAt.LocalDateTime), DateOnly.FromDateTime(now.LocalDateTime));
    }

    private static string FormatWholeUnits(int count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";

    public static string FormatCardTooltip(VideoSummary video)
    {
        var publishedText = video.PublishedAt is { } pub
            ? pub.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
            : video.ApproximateUploadDate is { } upload
                ? upload.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
                : null;
        return string.IsNullOrWhiteSpace(publishedText)
            ? $"{video.Title}\n{video.ChannelName}"
            : $"{video.Title}\n{video.ChannelName}\nPublished: {publishedText}";
    }
}
