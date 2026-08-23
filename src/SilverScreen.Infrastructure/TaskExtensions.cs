using System.Runtime.CompilerServices;
using Serilog;

namespace SilverScreen.Infrastructure;

public static class TaskExtensions
{
    private static readonly ILogger DefaultLogger = Log.ForContext(typeof(TaskExtensions));

    public static void FireAndForget(
        this Task? task,
        ILogger? logger = null,
        string? message = null,
        [CallerMemberName] string? caller = null)
    {
        if (task is null)
            return;

        if (task.IsCompletedSuccessfully)
            return;

        _ = HandleFaultAsync(task, logger ?? DefaultLogger, message, caller);
    }

    public static void FireAndForget(
        this ValueTask task,
        ILogger? logger = null,
        string? message = null,
        [CallerMemberName] string? caller = null)
    {
        if (task.IsCompletedSuccessfully)
            return;

        task.AsTask().FireAndForget(logger, message, caller);
    }

    private static async Task HandleFaultAsync(Task task, ILogger logger, string? message, string? caller)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Intentional cancellation of fire-and-forget tasks is normal lifecycle behavior.
        }
        catch (Exception exception)
        {
            if (string.IsNullOrWhiteSpace(message))
                logger.Error(exception, "Unhandled exception in fire-and-forget task ({Caller})", caller);
            else
                logger.Error(exception, "{Message} ({Caller})", message, caller);
        }
    }
}