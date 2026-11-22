namespace SilverScreen.Shell;

public sealed class ShellRefreshCoordinator : IShellRefreshCoordinator
{
    private Func<Task>? _refreshHandler;
    private Func<bool>? _isLoadingHandler;

    public bool IsLoading => _isLoadingHandler?.Invoke() ?? false;

    public event EventHandler<bool>? LoadingChanged;

    public void RegisterHandler(Func<Task> refreshHandler, Func<bool> isLoadingHandler)
    {
        _refreshHandler = refreshHandler ?? throw new ArgumentNullException(nameof(refreshHandler));
        _isLoadingHandler = isLoadingHandler ?? throw new ArgumentNullException(nameof(isLoadingHandler));
        NotifyLoadingChanged();
    }

    public Task RefreshActivePageAsync()
    {
        return _refreshHandler is not null ? _refreshHandler() : Task.CompletedTask;
    }

    public void NotifyLoadingChanged()
    {
        LoadingChanged?.Invoke(this, IsLoading);
    }
}
