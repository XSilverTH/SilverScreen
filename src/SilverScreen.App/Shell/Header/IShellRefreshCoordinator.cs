namespace SilverScreen.Shell.Header;

/// <summary>
///     Coordinates refresh actions and loading states between shell header buttons and the active view stack page.
/// </summary>
public interface IShellRefreshCoordinator
{
    bool IsLoading { get; }
    event EventHandler<bool>? LoadingChanged;
    Task RefreshActivePageAsync();
    void RegisterHandler(Func<Task> refreshHandler, Func<bool> isLoadingHandler);
    void NotifyLoadingChanged();
}
