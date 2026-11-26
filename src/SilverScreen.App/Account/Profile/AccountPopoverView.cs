using Adw;
using Gtk;
using SilverScreen.Account.Auth;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Account.Profile;

public partial class AccountPopoverView : ViewBase<Bin, AccountViewModel>
{
    private readonly INativeAuthService _authService;
    private readonly INotificationService _notifications;
    private readonly IThumbnailService _thumbnails;
    private AccountManualSessionEditorView? _manualEditor;
    private AccountSignedInCardView? _signedInCard;
    private AccountStatusPageView? _statusPage;
    private bool _editing;

    public AccountPopoverView(
        IThumbnailService thumbnails,
        INativeAuthService authService,
        INotificationService notifications)
    {
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Lifetime.Track(() => Widget.OnUnmap += OnUnmap, () => Widget.OnUnmap -= OnUnmap);
    }

    protected override void BindViewModel(AccountViewModel viewModel, BindingScope<AccountViewModel> bindings)
    {
        if (_statusPage is not null) _statusPage.ViewModel = viewModel;
        if (_signedInCard is not null) _signedInCard.ViewModel = viewModel;
        if (_manualEditor is not null) _manualEditor.ViewModel = viewModel;

        bindings.Bind(nameof(AccountViewModel.PresentationStatus), this,
            static vm => vm.PresentationStatus,
            static (view, _) => view.UpdateStack());
    }

    private void UpdateStack()
    {
        if (_editing)
        {
            EnsureManualEditor();
            account_stack.VisibleChildName = "manual";
            return;
        }

        var isReady = ViewModel?.PresentationStatus == AccountProfilePresentationStatus.Ready;
        if (isReady)
        {
            EnsureSignedInCard();
            account_stack.VisibleChildName = "signed_in";
        }
        else
        {
            EnsureStatusPage();
            account_stack.VisibleChildName = "status";
        }
    }

    private AccountStatusPageView EnsureStatusPage()
    {
        if (_statusPage is not null)
            return _statusPage;

        var statusPage = Lifetime.Own(new AccountStatusPageView(_authService, _notifications));
        statusPage.OpenManualEditorRequested += OnOpenManualEditorRequested;
        status_host.Child = statusPage.Widget;
        statusPage.ViewModel = ViewModel;
        _statusPage = statusPage;
        return statusPage;
    }

    private AccountSignedInCardView EnsureSignedInCard()
    {
        if (_signedInCard is not null)
            return _signedInCard;

        var signedInCard = Lifetime.Own(new AccountSignedInCardView(_thumbnails));
        signed_in_host.Child = signedInCard.Widget;
        signedInCard.ViewModel = ViewModel;
        _signedInCard = signedInCard;
        return signedInCard;
    }

    private AccountManualSessionEditorView EnsureManualEditor()
    {
        if (_manualEditor is not null)
            return _manualEditor;

        var manualEditor = Lifetime.Own(new AccountManualSessionEditorView());
        manualEditor.EditingCompleted += OnEditingCompleted;
        manual_host.Child = manualEditor.Widget;
        manualEditor.ViewModel = ViewModel;
        _manualEditor = manualEditor;
        return manualEditor;
    }

    private void OnOpenManualEditorRequested(object? sender, EventArgs e)
    {
        ShowManualEditor();
    }

    private void OnEditingCompleted(object? sender, EventArgs e)
    {
        ExitManualEditor();
    }

    private void ShowManualEditor()
    {
        _editing = true;
        EnsureManualEditor().Reset();
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
        _manualEditor?.Reset();
        UpdateStack();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_statusPage is not null)
                _statusPage.OpenManualEditorRequested -= OnOpenManualEditorRequested;
            if (_manualEditor is not null)
                _manualEditor.EditingCompleted -= OnEditingCompleted;
        }

        base.Dispose(disposing);
    }
}