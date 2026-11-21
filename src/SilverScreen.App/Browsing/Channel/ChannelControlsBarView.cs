using Gtk;
using Serilog;
using SilverScreen.Core.Browsing;
using SilverScreen.Core.Browsing.Channel;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelControlsBarView : ViewBase<Box, ChannelViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<ChannelControlsBarView>();
    private readonly ChannelAboutPopoverView _aboutPopover;
    private readonly Func<int>? _getBatchSize;
    private bool _updatingSortDropdown;

    public ChannelControlsBarView(Func<int>? getBatchSize = null)
    {
        _getBatchSize = getBatchSize;
        _aboutPopover = Lifetime.Own(new ChannelAboutPopoverView());
        channel_about_button.Popover = _aboutPopover.Widget;
    }

    protected override void BindViewModel(ChannelViewModel viewModel, BindingScope<ChannelViewModel> bindings)
    {
        _aboutPopover.ViewModel = viewModel;
        bindings.Bind(
            nameof(ChannelViewModel.State),
            this,
            static vm => vm.State.Sort,
            static (view, sort) => view.UpdateSort(sort));
    }

    private void UpdateSort(ChannelVideoSort sort)
    {
        _updatingSortDropdown = true;
        try
        {
            var sortIndex = sort switch
            {
                ChannelVideoSort.Oldest => 1u,
                ChannelVideoSort.Popular => 2u,
                _ => 0u
            };
            if (channel_sort_dropdown.Selected != sortIndex)
                channel_sort_dropdown.Selected = sortIndex;
        }
        finally
        {
            _updatingSortDropdown = false;
        }
    }

    private async void OnSortDropdownNotify(object? sender = null, EventArgs? args = null)
    {
        try
        {
            if (_updatingSortDropdown || IsDisposed || ViewModel is null) return;
            var selected = channel_sort_dropdown.Selected;
            var count = _getBatchSize?.Invoke() ?? VideoFeedConstants.DefaultPageSize;
            await ViewModel.SetSortSelection(selected, count);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to update channel sort selection");
        }
    }
}
