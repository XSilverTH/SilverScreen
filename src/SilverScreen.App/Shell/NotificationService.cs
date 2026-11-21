using Adw;
using Serilog;
using SilverScreen.Core.Common;
using Functions = GLib.Functions;

namespace SilverScreen.Shell;

/// <summary>
///     Thread-safe presentation notification service that marshals toast notifications to the Libadwaita ToastOverlay.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private static readonly ILogger Logger = Log.ForContext<NotificationService>();
    private ToastOverlay? _toastOverlay;

    /// <summary>Gets a value indicating whether a ToastOverlay is currently attached.</summary>
    public bool IsOverlayAttached => _toastOverlay is not null;

    /// <summary>
    ///     Attaches an active <see cref="ToastOverlay"/> to display notifications.
    /// </summary>
    public void AttachToastOverlay(ToastOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _toastOverlay = overlay;
        Logger.Debug("Attached ToastOverlay to NotificationService");
    }

    /// <summary>
    ///     Detaches the current <see cref="ToastOverlay"/>, preventing further notifications from being displayed on closed views.
    /// </summary>
    public void DetachToastOverlay(ToastOverlay? overlay = null)
    {
        if (overlay is null || ReferenceEquals(_toastOverlay, overlay))
        {
            _toastOverlay = null;
            Logger.Debug("Detached ToastOverlay from NotificationService");
        }
    }

    /// <inheritdoc />
    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var overlay = _toastOverlay;
        if (overlay is null)
        {
            Logger.Debug("Toast dropped (no overlay attached): {Message}", message);
            return;
        }

        var text = DiagnosticSanitizer.Sanitize(message.Trim());
        Logger.Debug("Enqueuing toast notification: {Message}", text);
        Functions.IdleAdd(0, () =>
        {
            if (ReferenceEquals(_toastOverlay, overlay))
                overlay.AddToast(Toast.New(text));

            return false;
        });
    }

    /// <inheritdoc />
    public void ShowToast(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);

        var overlay = _toastOverlay;
        if (overlay is null)
        {
            Logger.Debug("Toast dropped (no overlay attached)");
            return;
        }

        Logger.Debug("Enqueuing toast instance");
        Functions.IdleAdd(0, () =>
        {
            if (ReferenceEquals(_toastOverlay, overlay))
                overlay.AddToast(toast);

            return false;
        });
    }
}
