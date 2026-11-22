namespace SilverScreen.Shell.Notifications;

/// <summary>
///     Contract for presenting user notifications and transient toast messages.
/// </summary>
public interface INotificationService
{
    /// <summary>
    ///     Shows a transient text toast to the user, sanitizing sensitive content and marshalling to the main thread.
    /// </summary>
    void ShowToast(string message);

    /// <summary>
    ///     Shows an existing Libadwaita toast instance, marshalling to the main thread.
    /// </summary>
    void ShowToast(Adw.Toast toast);
}
