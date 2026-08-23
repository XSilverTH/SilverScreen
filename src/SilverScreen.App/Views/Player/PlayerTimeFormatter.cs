namespace SilverScreen.Views.Player;

internal static class PlayerTimeFormatter
{
    public static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (long)Math.Floor(value.TotalSeconds));
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}