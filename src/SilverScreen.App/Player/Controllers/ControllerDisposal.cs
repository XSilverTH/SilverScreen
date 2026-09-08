using Gtk;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Shared Gtk controller lifecycle helper: dispose-guard, host attach/detach,
///     GLib timeout cleanup, and cancellation cleanup. Wave 1 consolidation of the
///     Add/RemoveController + dispose-flag boilerplate duplicated across player controllers.
/// </summary>
internal static class ControllerDisposal
{
    /// <summary>
    ///     Atomically claims disposal. Returns false when already disposed (caller must return).
    /// </summary>
    public static bool TryBeginDispose(ref bool disposed)
    {
        if (disposed) return false;
        disposed = true;
        return true;
    }

    public static void Attach(Widget host, EventController controller)
    {
        host.AddController(controller);
    }

    /// <summary>
    ///     Removes the controller from its host and disposes it.
    /// </summary>
    public static void Detach(Widget host, EventController controller)
    {
        host.RemoveController(controller);
        controller.Dispose();
    }

    /// <summary>
    ///     Removes a GLib timeout/idle source and zeroes the handle.
    /// </summary>
    public static void ClearTimeout(ref uint source)
    {
        if (source == 0) return;
        SourceRemove(source);
        source = 0;
    }

    /// <summary>
    ///     Cancels, disposes, and nulls a <see cref="CancellationTokenSource" /> holder.
    /// </summary>
    public static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }
}
