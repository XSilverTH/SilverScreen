using Gtk;
using SilverScreen.Browsing.Components;
using SilverScreen.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelView : ViewBase<Box, ChannelViewModel>
{
    private readonly ChannelHeaderView _header;
    private readonly VideoListView _videoList;

    public ChannelView(
        ChannelViewModel viewModel,
        VideoCardServices cardServices)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(cardServices);

        _videoList = Lifetime.Own(new VideoListView(viewModel, cardServices));
        channel_video_list_host.Append(_videoList.Widget);

        _header = Lifetime.Own(new ChannelHeaderView(cardServices.Thumbnails, () => _videoList.GetBatchSize()));
        channel_header_host.Append(_header.Widget);

        _header.AttachScrollTracking(_videoList.ScrolledWindow, _videoList.Vadjustment);

        Lifetime.Track(
            () => _videoList.RefreshLoadingChanged += OnVideoListRefreshLoadingChanged,
            () => _videoList.RefreshLoadingChanged -= OnVideoListRefreshLoadingChanged);

        ViewModel = viewModel;
    }

    public bool IsLoading => _videoList.IsLoading;
    public Adjustment? Vadjustment => _videoList.Vadjustment;

    protected override void BindViewModel(ChannelViewModel viewModel, BindingScope<ChannelViewModel> bindings)
    {
        _header.ViewModel = viewModel;
    }

    public event EventHandler<bool>? RefreshLoadingChanged;

    public int GetBatchSize()
    {
        return _videoList.GetBatchSize();
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _videoList.RefreshAsync().ConfigureAwait(false);
        }
        finally
        {
            GtkUiDispatch.Run(() => !IsDisposed, () => _header.SetHeaderCollapsed(false));
        }
    }

    private void OnVideoListRefreshLoadingChanged(object? sender, bool isLoading)
    {
        RefreshLoadingChanged?.Invoke(this, isLoading);
    }
}