namespace SilverScreen.Core.Player;

public readonly record struct PlaybackResult
{
    private PlaybackResult(bool success, string message, string? errorMessage)
    {
        Success = success;
        Message = message;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }
    public string? ErrorMessage { get; }
    public string Message { get; }

    public static PlaybackResult Ok(string message = "Playback started.")
    {
        return new PlaybackResult(true, message, null);
    }

    public static PlaybackResult Fail(string errorMessage)
    {
        return new PlaybackResult(false, errorMessage, errorMessage);
    }

    public static bool IsSuccessStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var s = status.Trim();
        return s.StartsWith("Opening in MPV", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Opening embedded player", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Playback started", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Embedded presenter called", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("External playback called", StringComparison.OrdinalIgnoreCase);
    }

    public static implicit operator string(PlaybackResult result)
    {
        return result.Message;
    }
}