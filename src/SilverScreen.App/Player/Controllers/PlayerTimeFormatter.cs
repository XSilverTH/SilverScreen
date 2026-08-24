namespace SilverScreen.Player.Controllers;

internal static class PlayerTimeFormatter
{
    public static string FormatTime(TimeSpan value) => PlayerTimelineEngine.FormatTime(value);
}