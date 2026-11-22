using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell;

/// <summary>
///     Encapsulates the shell refresh button and spinner for the active page.
/// </summary>
public partial class RefreshHeaderButtonView : ViewBase<Button>
{
    private static readonly ILogger Logger = Log.ForContext<RefreshHeaderButtonView>();
    private readonly IShellRefreshCoordinator _refreshCoordinator;

    public RefreshHeaderButtonView(IShellRefreshCoordinator refreshCoordinator)
    {
        _refreshCoordinator = refreshCoordinator ?? throw new ArgumentNullException(nameof(refreshCoordinator));

        Lifetime.Track(
            () => _refreshCoordinator.LoadingChanged += OnLoadingChanged,
            () => _refreshCoordinator.LoadingChanged -= OnLoadingChanged);

        UpdateState(_refreshCoordinator.IsLoading);
    }

    private void OnClicked(Button sender, EventArgs args)
    {
        _refreshCoordinator.RefreshActivePageAsync().FireAndForget(Logger);
    }

    private void OnLoadingChanged(object? sender, bool isLoading)
    {
        Lifetime.Idle(() =>
        {
            if (!IsDisposed) UpdateState(isLoading);
            return false;
        });
    }

    private void UpdateState(bool isLoading)
    {
        Widget.Sensitive = !isLoading;
        home_refresh_stack.VisibleChildName = isLoading ? "loading" : "idle";
        home_refresh_spinner.Spinning = isLoading;
    }
}
