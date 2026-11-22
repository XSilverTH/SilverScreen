using Gtk;
using Serilog;
using SilverScreen.Account.Auth;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Account.Profile;

public partial class AccountStatusPageView(INativeAuthService authService, INotificationService notifications)
    : ViewBase<Box, AccountViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<AccountStatusPageView>();

    private readonly INativeAuthService _authService =
        authService ?? throw new ArgumentNullException(nameof(authService));

    private readonly INotificationService _notifications =
        notifications ?? throw new ArgumentNullException(nameof(notifications));

    public event EventHandler? OpenManualEditorRequested;

    protected override void BindViewModel(AccountViewModel viewModel, BindingScope<AccountViewModel> bindings)
    {
        bindings.Bind(nameof(AccountViewModel.PresentationStatus), this,
            static vm => vm.PresentationStatus,
            static (view, status) => view.RenderStatus(status));
    }

    private void RenderStatus(AccountProfilePresentationStatus status)
    {
        switch (status)
        {
            case AccountProfilePresentationStatus.Checking:
                status_title.SetText("Checking your YouTube account");
                status_description.SetText("Your saved session is being verified.");
                SetVisibility(true, true, false, false, false, true);
                break;
            case AccountProfilePresentationStatus.Rejected:
                status_title.SetText("Sign in again");
                status_description.SetText(
                    "Your YouTube session has expired. Your saved credentials are kept until you choose to sign out.");
                google_button.AddCssClass("suggested-action");
                SetVisibility(true, true, false, true, true, true);
                break;
            case AccountProfilePresentationStatus.Unavailable:
                status_title.SetText("Couldn’t verify your account");
                status_description.SetText(
                    "YouTube couldn’t verify your account. Your saved session is kept. Try again.");
                google_button.RemoveCssClass("suggested-action");
                SetVisibility(true, true, true, true, true, true);
                break;
            case AccountProfilePresentationStatus.SignedOut:
            default:
                google_button.AddCssClass("suggested-action");
                SetVisibility(false, false, false, true, true, false);
                break;
        }
    }

    private void SetVisibility(bool title, bool desc, bool retry, bool google, bool manual, bool signOut)
    {
        status_title.Visible = title;
        status_description.Visible = desc;
        retry_button.Visible = retry;
        google_button.Visible = google;
        manual_button.Visible = manual;
        sign_out_button.Visible = signOut;
    }

    private void OnGoogleSignInClicked(Button sender, EventArgs args)
    {
        CloseHostPopover();
        StartNativeAuthAsync().FireAndForget(Logger);
    }

    private async Task StartNativeAuthAsync()
    {
        try
        {
            await _authService.StartAuthAsync().ConfigureAwait(false);
        }
        catch (SessionPersistenceException)
        {
            _notifications.ShowToast("Could not save the YouTube session because the system keyring is unavailable.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to run native auth");
            _notifications.ShowToast("An error occurred during sign-in.");
        }
    }

    private void OnManualCookiesClicked(Button sender, EventArgs args)
    {
        OpenManualEditorRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRetryClicked(Button sender, EventArgs args)
    {
        ViewModel?.RefreshProfile();
    }

    private void OnSignOutClicked(Button sender, EventArgs args)
    {
        ViewModel?.ClearSessionAsync().FireAndForget(Logger);
    }

    private void CloseHostPopover()
    {
        var parent = Widget.Parent;
        while (parent is not null)
        {
            if (parent is Popover popover)
            {
                popover.Popdown();
                return;
            }

            parent = parent.Parent;
        }
    }
}