using Gtk;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Popovers;

public partial class AccountPopoverView : ViewBase<Box>
{
    private readonly AccountViewModel _viewModel;
    private readonly Action _openWebLogin;
    private readonly Action<bool> _sessionAppearanceChanged;
    private bool _editing;
    private bool _disposed;

    public AccountPopoverView(
        AccountViewModel viewModel,
        Action openWebLogin,
        Action<bool> sessionAppearanceChanged)
    {
        _viewModel = viewModel;
        _openWebLogin = openWebLogin;
        _sessionAppearanceChanged = sessionAppearanceChanged;
        _viewModel.StateChanged += OnStateChanged;
        Render();
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render();

            return false;
        });
    }

    private void Render()
    {
        _sessionAppearanceChanged(_viewModel.HasManualSession);
        Clear(Widget);
        if (_editing)
        {
            RenderEditor();
            return;
        }

        var heading = Label.New(_viewModel.HasManualSession ? "YouTube session" : "Not signed in");
        heading.Xalign = 0;
        heading.CssClasses = ["heading"];
        Widget.Append(heading);
        if (_viewModel.HasManualSession)
        {
            Widget.Append(DimLabel(
                "YouTube session active. Cookies are stored in your desktop Secret Service keyring."));
            Widget.Append(SuggestedActionButton("Refresh with Google", _openWebLogin));
            var validate = ActionButton("Validate Home session", () => _ = _viewModel.ValidateAsync());
            validate.Sensitive = !_viewModel.IsValidating;
            Widget.Append(validate);
            Widget.Append(ActionButton("Replace with manual session", OpenManualEditor));
            Widget.Append(ActionButton("Clear session", _viewModel.ClearSession));
        }
        else
        {
            Widget.Append(DimLabel(
                "Sign in with Google in an isolated window, or paste Netscape cookies.txt content manually. Values are not displayed after saving and are stored securely in your desktop keyring."));
            Widget.Append(SuggestedActionButton("Sign in with Google", _openWebLogin));
            Widget.Append(ActionButton("Add manual session", OpenManualEditor));
        }
    }

    private void OpenManualEditor()
    {
        _editing = true;
        Render();
    }

    private void RenderEditor()
    {
        var heading = Label.New(_viewModel.HasManualSession ? "Replace with manual session" : "Add manual session");
        heading.Xalign = 0;
        heading.CssClasses = ["heading"];
        Widget.Append(heading);
        Widget.Append(DimLabel(
            "Paste Netscape cookies.txt content exported from a browser. SilverScreen stores it in your desktop Secret Service keyring, writes temporary subprocess cookie files with user-only permissions, and removes them when practical."));
        var editor = TextView.New();
        editor.Monospace = true;
        editor.WrapMode = WrapMode.Char;
        editor.HeightRequest = 180;
        var scrolled = ScrolledWindow.New();
        scrolled.Hexpand = true;
        scrolled.WidthRequest = 440;
        scrolled.HeightRequest = 180;
        scrolled.Child = editor;
        Widget.Append(scrolled);
        var actions = Box.New(Orientation.Horizontal, 6);
        actions.Halign = Align.End;
        actions.Append(ActionButton("Cancel", () =>
        {
            _editing = false;
            Render();
        }));
        var save = Button.NewWithLabel("Save session");
        save.CssClasses = ["suggested-action"];
        save.OnClicked += (_, _) =>
        {
            if (_viewModel.SaveManualSession(GetText(editor)))
            {
                _editing = false;
                Render();
            }
        };
        actions.Append(save);
        Widget.Append(actions);
    }

    private static string GetText(TextView textView)
    {
        var buffer = textView.Buffer ??
                     throw new InvalidOperationException("Manual session editor text buffer was not initialized.");
        buffer.GetBounds(out var start, out var end);
        return buffer.GetText(start, end, true);
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = Button.NewWithLabel(label);
        button.Halign = Align.Fill;
        button.CssClasses = ["flat"];
        button.OnClicked += (_, _) => action();
        return button;
    }

    private static Button SuggestedActionButton(string label, Action action)
    {
        var button = ActionButton(label, action);
        button.CssClasses = ["suggested-action"];
        return button;
    }

    private static Label DimLabel(string text)
    {
        var label = Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.CssClasses = ["dim-label"];
        return label;
    }

    private static void Clear(Box box)
    {
        while (box.GetFirstChild() is { } child)
            box.Remove(child);
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