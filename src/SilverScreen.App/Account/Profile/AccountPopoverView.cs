using Adw;
using Gtk;
using SilverScreen.Account.Auth;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Account.Profile;

public partial class AccountPopoverView : ViewBase<Bin, AccountViewModel>
{
    private readonly AccountManualSessionEditorView _manualEditor;
    private readonly AccountSignedInCardView _signedInCard;
    private readonly AccountStatusPageView _statusPage;
    private bool _editing;

    public AccountPopoverView(
        IThumbnailService thumbnails,
        INativeAuthService authService,
        INotificationService notifications)
    {
        _statusPage = Lifetime.Own(new AccountStatusPageView(authService, notifications));
        _signedInCard = Lifetime.Own(new AccountSignedInCardView(thumbnails));
        _manualEditor = Lifetime.Own(new AccountManualSessionEditorView());

        status_host.Child = _statusPage.Widget;
        signed_in_host.Child = _signedInCard.Widget;
        manual_host.Child = _manualEditor.Widget;

        _statusPage.OpenManualEditorRequested += (_, _) => ShowManualEditor();
        _manualEditor.EditingCompleted += (_, _) => ExitManualEditor();
        Lifetime.Track(() => Widget.OnUnmap += OnUnmap, () => Widget.OnUnmap -= OnUnmap);
    }

    protected override void BindViewModel(AccountViewModel viewModel, BindingScope<AccountViewModel> bindings)
    {
        _statusPage.ViewModel = viewModel;
        _signedInCard.ViewModel = viewModel;
        _manualEditor.ViewModel = viewModel;

        bindings.Bind(nameof(AccountViewModel.PresentationStatus), this,
            static vm => vm.PresentationStatus,
            static (view, _) => view.UpdateStack());
    }

    private void UpdateStack()
    {
        if (_editing)
        {
            account_stack.VisibleChildName = "manual";
            return;
        }

        var isReady = ViewModel?.PresentationStatus == AccountProfilePresentationStatus.Ready;
        account_stack.VisibleChildName = isReady ? "signed_in" : "status";
    }

    private void ShowManualEditor()
    {
        _editing = true;
        _manualEditor.Reset();
        account_stack.VisibleChildName = "manual";
    }

    private void ExitManualEditor()
    {
        _editing = false;
        UpdateStack();
    }

    private void OnUnmap(Widget sender, EventArgs args)
    {
        if (!_editing) return;
        _editing = false;
        _manualEditor.Reset();
        UpdateStack();
    }
}