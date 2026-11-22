using Gtk;
using Serilog;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell;

/// <summary>
///     Encapsulates the runtime dependency setup warning button and popover.
/// </summary>
public partial class DependencyWarningButtonView : ViewBase<MenuButton>
{
    private static readonly ILogger Logger = Log.ForContext<DependencyWarningButtonView>();

    public DependencyWarningButtonView(RuntimeDependencyDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var warnings = diagnostics.GetStartupWarnings();
        if (warnings.Count == 0)
        {
            Widget.Visible = false;
            return;
        }

        Logger.Warning("Runtime setup needed: {Warnings}", string.Join(" ", warnings));
        Widget.Visible = true;

        var popoverContent = Box.New(Orientation.Vertical, 12);
        popoverContent.MarginTop = popoverContent.MarginBottom = popoverContent.MarginStart = popoverContent.MarginEnd = 16;

        var heading = Label.New("Runtime setup needed");
        heading.AddCssClass("heading");
        heading.Xalign = 0;
        popoverContent.Append(heading);

        foreach (var warning in warnings)
        {
            var warningLabel = Label.New(warning);
            warningLabel.Wrap = true;
            warningLabel.MaxWidthChars = 48;
            warningLabel.Xalign = 0;
            popoverContent.Append(warningLabel);
        }

        var preferencesButton = Button.NewWithLabel("Open Preferences");
        preferencesButton.ActionName = "win.preferences";
        preferencesButton.OnClicked += (_, _) => deps_popover.Popdown();
        popoverContent.Append(preferencesButton);

        deps_popover.Child = popoverContent;
    }
}
