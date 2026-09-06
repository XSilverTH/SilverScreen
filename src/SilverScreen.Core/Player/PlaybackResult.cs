namespace SilverScreen.Core.Player;

public readonly record struct PlaybackResult
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public string Message { get; }

    private PlaybackResult(bool success, string message, string? errorMessage)
    {
        Success = success;
        Message = message;
        ErrorMessage = errorMessage;
    }

    public static PlaybackResult Ok(string message = "Playback started.") =>
        new(true, message, null);

    public static PlaybackResult Fail(string errorMessage) =>
        new(false, errorMessage, errorMessage);

    public static PlaybackResult FromStatusString(string status)
    {
        if (IsSuccessStatus(status))
            return Ok(status);

        return Fail(status);
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

    public static implicit operator string(PlaybackResult result) => result.Message;
}
