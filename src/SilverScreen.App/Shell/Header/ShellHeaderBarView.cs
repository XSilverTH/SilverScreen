using Adw;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Domain;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;
using HeaderBar = Adw.HeaderBar;

namespace SilverScreen.Shell.Header;

/// <summary>
///     Structural host container for the application's top Adw.HeaderBar and child widgets.
/// </summary>
public partial class ShellHeaderBarView : ViewBase<HeaderBar>
{
    public ShellHeaderBarView(
        INavigationService navigationService,
        IPlaybackModeRoutingService playback,
        AccountServices account,
        AccountViewModel accountViewModel,
        SearchViewModel searchViewModel,
        IShellRefreshCoordinator refreshCoordinator,
        RuntimeDependencyDiagnostics diagnostics,
        INotificationService notifications,
        QueueViewModel queueViewModel,
        IThumbnailService thumbnails)
        : this(
            new NavigationBackButtonView(navigationService),
            new AccountHeaderButtonView(accountViewModel, thumbnails, account.Auth, notifications),
            new SearchHeaderButtonView(searchViewModel, navigationService, notifications),
            new NowPlayingHeaderBarView(playback, navigationService, account.Preferences),
            new QueueHeaderButtonView(queueViewModel),
            new RefreshHeaderButtonView(refreshCoordinator),
            new DependencyWarningButtonView(diagnostics))
    {
    }

    private ShellHeaderBarView(
        NavigationBackButtonView backButton,
        AccountHeaderButtonView accountButton,
        SearchHeaderButtonView searchButton,
        NowPlayingHeaderBarView nowPlaying,
        QueueHeaderButtonView queueButton,
        RefreshHeaderButtonView refreshButton,
        DependencyWarningButtonView depsButton)
    {
        BackButton = Lifetime.Own(backButton ?? throw new ArgumentNullException(nameof(backButton)));
        AccountButton = Lifetime.Own(accountButton ?? throw new ArgumentNullException(nameof(accountButton)));
        SearchButton = Lifetime.Own(searchButton ?? throw new ArgumentNullException(nameof(searchButton)));
        NowPlaying = Lifetime.Own(nowPlaying ?? throw new ArgumentNullException(nameof(nowPlaying)));
        QueueButton = Lifetime.Own(queueButton ?? throw new ArgumentNullException(nameof(queueButton)));
        RefreshButton = Lifetime.Own(refreshButton ?? throw new ArgumentNullException(nameof(refreshButton)));
        DepsButton = Lifetime.Own(depsButton ?? throw new ArgumentNullException(nameof(depsButton)));

        // Pack start children (left to right)
        Widget.PackStart(BackButton.Widget);
        Widget.PackStart(AccountButton.Widget);
        Widget.PackStart(SearchButton.Widget);

        // Pack end children (inward from window controls on right edge)
        Widget.PackEnd(NowPlaying.Widget);
        Widget.PackEnd(QueueButton.Widget);
        Widget.PackEnd(RefreshButton.Widget);
        Widget.PackEnd(DepsButton.Widget);
    }

    private NavigationBackButtonView BackButton { get; }
    private AccountHeaderButtonView AccountButton { get; }
    private SearchHeaderButtonView SearchButton { get; }
    private NowPlayingHeaderBarView NowPlaying { get; }
    public QueueHeaderButtonView QueueButton { get; }
    private RefreshHeaderButtonView RefreshButton { get; }
    private DependencyWarningButtonView DepsButton { get; }
    public ViewSwitcher ViewSwitcher => view_switcher;

    /// <summary>Controls visibility of the title view switcher during responsive breakpoint shifts.</summary>
    public void SetTitleWidgetRevealed(bool revealed)
    {
        Widget.TitleWidget = revealed ? view_switcher : null;
    }
}