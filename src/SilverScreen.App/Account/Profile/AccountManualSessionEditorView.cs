using Gtk;
using Serilog;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Account.Profile;

public partial class AccountManualSessionEditorView : ViewBase<Box, AccountViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<AccountManualSessionEditorView>();

    public event EventHandler? EditingCompleted;

    protected override void BindViewModel(AccountViewModel viewModel, BindingScope<AccountViewModel> bindings)
    {
        bindings.Bind(nameof(AccountViewModel.HasManualSession), this,
            static vm => vm.HasManualSession,
            static (view, hasSession) => view.UpdateHeading(hasSession));
    }

    public void Reset()
    {
        ClearBuffer();
        manual_error_label.SetVisible(false);
        UpdateHeading(ViewModel?.HasManualSession ?? false);
    }

    private void UpdateHeading(bool hasSession)
    {
        manual_heading.SetText(hasSession ? "Replace with manual session" : "Add manual session");
    }

    private void OnCancelClicked(Button sender, EventArgs args)
    {
        Reset();
        EditingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async void OnSaveClicked(Button sender, EventArgs args)
    {
        try
        {
            var text = GetText();
            if (ViewModel is null) return;

            if (!await ViewModel.SaveManualSessionAsync(text))
            {
                manual_error_label.SetText(ViewModel.ManualSessionError ?? "Could not save the session. Try again.");
                manual_error_label.SetVisible(true);
                return;
            }

            Reset();
            EditingCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to save manual session");
        }
    }

    private string GetText()
    {
        var buffer = manual_editor.Buffer ?? throw new InvalidOperationException("Text buffer not initialized.");
        buffer.GetBounds(out var start, out var end);
        return buffer.GetText(start, end, true);
    }

    private void ClearBuffer()
    {
        manual_editor.Buffer?.SetText(string.Empty, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearBuffer();
        base.Dispose(disposing);
    }
}