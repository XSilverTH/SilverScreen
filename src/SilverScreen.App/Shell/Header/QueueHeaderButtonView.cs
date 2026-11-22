using Adw;
using GObject;
using Gtk;
using SilverScreen.Queue;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell.Header;

/// <summary>
///     Encapsulates the queue toggle button and count badge in the shell header bar.
/// </summary>
public partial class QueueHeaderButtonView : ViewBase<ToggleButton, QueueViewModel>
{
    public QueueHeaderButtonView(QueueViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override void BindViewModel(QueueViewModel viewModel, BindingScope<QueueViewModel> bindings)
    {
        bindings.Bind(
            nameof(QueueViewModel.State),
            queue_button_label,
            static vm => vm.State.Items.Count.ToString(),
            static (label, countText) => label.SetText(countText));
    }

    public void BindToSplitView(OverlaySplitView splitView)
    {
        ArgumentNullException.ThrowIfNull(splitView);
        Widget.BindProperty("active", splitView, "show-sidebar",
            BindingFlags.Bidirectional | BindingFlags.SyncCreate);
    }

    public void CloseQueue()
    {
        Widget.Active = false;
    }
}
