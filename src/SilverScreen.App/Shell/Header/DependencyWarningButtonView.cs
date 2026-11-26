using Gtk;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Object = GObject.Object;

namespace SilverScreen.Shell.Header;

/// <summary>
///     Encapsulates the runtime dependency setup warning button and popover.
/// </summary>
public partial class DependencyWarningButtonView : ViewBase<MenuButton>
{
    private static readonly ILogger Logger = Log.ForContext<DependencyWarningButtonView>();
    private readonly RuntimeDependencyDiagnostics _diagnostics;
    private readonly ISessionService? _sessionService;
    private long _evaluationGeneration;
    private string? _loggedWarningsKey;
    private bool _popoverBuilt;
    private Button? _preferencesButton;
    private IReadOnlyList<string> _warnings = [];

    public DependencyWarningButtonView(
        RuntimeDependencyDiagnostics diagnostics,
        ISessionService? sessionService = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _sessionService = sessionService;

        Widget.Visible = false;

        if (_sessionService is not null)
            Lifetime.Track(
                () => _sessionService.SessionChanged += OnSessionChanged,
                () => _sessionService.SessionChanged -= OnSessionChanged);

        Lifetime.Track(
            () => deps_popover.OnNotify += OnPopoverNotify,
            () => deps_popover.OnNotify -= OnPopoverNotify);

        Functions.IdleAdd(0, () =>
        {
            if (!IsDisposed) RefreshWarningsAsync().FireAndForget(Logger);
            return false;
        });
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed) RefreshWarningsAsync().FireAndForget(Logger);
    }

    private async Task RefreshWarningsAsync()
    {
        var generation = Interlocked.Increment(ref _evaluationGeneration);
        var warnings = await Task.Run(() => _diagnostics.GetStartupWarnings()).ConfigureAwait(false);
        if (IsDisposed || generation != Volatile.Read(ref _evaluationGeneration))
            return;

        Lifetime.Idle(() =>
        {
            if (IsDisposed || generation != Volatile.Read(ref _evaluationGeneration))
                return false;

            ApplyWarnings(warnings);
            return false;
        });
    }

    private void ApplyWarnings(IReadOnlyList<string> warnings)
    {
        if (_warnings.SequenceEqual(warnings, StringComparer.Ordinal))
            return;

        _warnings = warnings;
        _popoverBuilt = false;
        ClearPreferencesButtonHandler();

        if (warnings.Count == 0)
        {
            _loggedWarningsKey = null;
            deps_popover.Child = null!;
            Widget.Visible = false;
            return;
        }

        var joined = string.Join(" ", warnings);
        if (!string.Equals(_loggedWarningsKey, joined, StringComparison.Ordinal))
        {
            _loggedWarningsKey = joined;
            Logger.Warning("Runtime setup needed: {Warnings}", joined);
        }

        Widget.Visible = true;
        if (deps_popover.GetVisible())
            EnsurePopoverContent();
    }

    private void OnPopoverNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "visible" && deps_popover.GetVisible() && !IsDisposed)
            EnsurePopoverContent();
    }

    private void EnsurePopoverContent()
    {
        if (_popoverBuilt || _warnings.Count == 0)
            return;

        ClearPreferencesButtonHandler();

        var popoverContent = Box.New(Orientation.Vertical, 12);
        popoverContent.MarginTop =
            popoverContent.MarginBottom = popoverContent.MarginStart = popoverContent.MarginEnd = 16;

        var heading = Label.New("Runtime setup needed");
        heading.AddCssClass("heading");
        heading.Xalign = 0;
        popoverContent.Append(heading);

        foreach (var warning in _warnings)
        {
            var warningLabel = Label.New(warning);
            warningLabel.Wrap = true;
            warningLabel.MaxWidthChars = 48;
            warningLabel.Xalign = 0;
            popoverContent.Append(warningLabel);
        }

        var preferencesButton = Button.NewWithLabel("Open Preferences");
        preferencesButton.ActionName = "win.preferences";
        preferencesButton.OnClicked += OnPreferencesButtonClicked;
        _preferencesButton = preferencesButton;
        popoverContent.Append(preferencesButton);

        deps_popover.Child = popoverContent;
        _popoverBuilt = true;
    }

    private void OnPreferencesButtonClicked(Button sender, EventArgs args)
    {
        deps_popover.Popdown();
    }

    private void ClearPreferencesButtonHandler()
    {
        if (_preferencesButton is null)
            return;

        _preferencesButton.OnClicked -= OnPreferencesButtonClicked;
        _preferencesButton = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearPreferencesButtonHandler();

        base.Dispose(disposing);
    }
}