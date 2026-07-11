using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Popovers;

public partial class QueuePopoverView : ViewBase<Box, QueueViewModel>
{
    private readonly Label _duration;
    private readonly Box _items;
    private bool _disposed;

    public QueuePopoverView(QueueViewModel viewModel)
    {
        _duration = GetRequiredObject<Label>("queue_duration_label");
        _items = GetRequiredObject<Box>("queue_items_box");
        viewModel.StateChanged += OnStateChanged;
        ViewModel = viewModel;
        Render(viewModel.State);
    }

    protected override void BindViewModel(QueueViewModel viewModel, BindingScope<QueueViewModel> bindings)
    {
        bindings.Bind(nameof(QueueViewModel.State), _duration, static model => FormatDuration(model.State.TotalDuration),
            static (label, value) => label.SetText(value));
    }

    private void OnClearQueueButtonClicked(object? sender, EventArgs args) => ViewModel?.Clear();

    private void OnStateChanged(object? sender, QueuePresentationState state)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
            {
                Render(state);
            }

            return false;
        });
    }

    private void Render(QueuePresentationState state)
    {
        Clear(_items);
        foreach (var item in state.Items)
        {
            _items.Append(CreateRow(item));
        }
    }

    private Widget CreateRow(QueueItem item)
    {
        var row = Box.New(Orientation.Horizontal, 8);
        row.WidthRequest = 320;
        var title = Label.New(item.Video.Title);
        title.Xalign = 0;
        title.Hexpand = true;
        title.Wrap = true;
        title.MaxWidthChars = 34;
        row.Append(title);
        var remove = Button.NewFromIconName("user-trash-symbolic");
        remove.TooltipText = "Remove from queue";
        remove.CssClasses = ["flat", "circular"];
        remove.OnClicked += (_, _) => ViewModel?.Remove(item);
        row.Append(remove);
        return row;
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m"
            : $"{duration.Seconds}s";

    private static void Clear(Box box)
    {
        while (box.GetFirstChild() is { } child)
        {
            box.Remove(child);
        }
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ViewModel is { } viewModel)
        {
            viewModel.StateChanged -= OnStateChanged;
            viewModel.Dispose();
        }

        base.Dispose();
    }
}
