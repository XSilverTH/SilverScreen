using Gtk;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Popovers;

public partial class AccountPopoverView : ViewBase<Box>
{
    private readonly Stack _accountStack;
    private readonly TextView _manualEditor;
    private readonly Label _manualHeading;
    private readonly Action _openWebLogin;
    private readonly Action<bool> _sessionAppearanceChanged;
    private readonly Button _signedInValidateButton;
    private readonly AccountViewModel _viewModel;
    private bool _disposed;
    private bool _editing;

    public AccountPopoverView(
        AccountViewModel viewModel,
        Action openWebLogin,
        Action<bool> sessionAppearanceChanged)
    {
        _accountStack = GetRequiredObject<Stack>("account_stack");
        _signedInValidateButton = GetRequiredObject<Button>("signed_in_validate_button");
        _manualHeading = GetRequiredObject<Label>("manual_heading");
        _manualEditor = GetRequiredObject<TextView>("manual_editor");
        _viewModel = viewModel;
        _openWebLogin = openWebLogin;
        _sessionAppearanceChanged = sessionAppearanceChanged;
        _viewModel.StateChanged += OnStateChanged;
        Render();
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render();

            return false;
        });
    }

    private void Render()
    {
        var hasManualSession = _viewModel.HasManualSession;
        _sessionAppearanceChanged(hasManualSession);
        if (_editing)
        {
            _accountStack.VisibleChildName = "manual";
            _manualHeading.SetText(hasManualSession
                ? "Replace with manual session"
                : "Add manual session");
            return;
        }

        _accountStack.VisibleChildName = hasManualSession ? "signed_in" : "signed_out";
        _signedInValidateButton.Sensitive = !_viewModel.IsValidating;
    }

    private void OpenManualEditor()
    {
        _editing = true;
        Render();
    }

    private void OnWebLoginClicked(object? sender, EventArgs args)
    {
        _openWebLogin();
    }

    private void OnOpenManualEditorClicked(object? sender, EventArgs args)
    {
        OpenManualEditor();
    }

    private void OnValidateButtonClicked(object? sender, EventArgs args)
    {
        _ = _viewModel.ValidateAsync();
    }

    private void OnClearButtonClicked(object? sender, EventArgs args)
    {
        _viewModel.ClearSession();
    }

    private void OnManualCancelButtonClicked(object? sender, EventArgs args)
    {
        _editing = false;
        Render();
    }

    private void OnManualSaveButtonClicked(object? sender, EventArgs args)
    {
        if (!_viewModel.SaveManualSession(GetText(_manualEditor))) return;
        _editing = false;
        Render();
    }

    private static string GetText(TextView textView)
    {
        var buffer = textView.Buffer ??
                     throw new InvalidOperationException("Manual session editor text buffer was not initialized.");
        buffer.GetBounds(out var start, out var end);
        return buffer.GetText(start, end, true);
    }


    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
        base.Dispose();
    }
}