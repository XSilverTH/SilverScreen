using Gtk;
using SilverScreen.Browsing.Components;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsView : ViewBase<Box, SubscriptionsViewModel>
{
    private readonly SubscriptionsChannelBarView _channelBar;
    private readonly VideoListView _videoList;

    public SubscriptionsView(
        SubscriptionsViewModel viewModel,
        VideoCardServices cardServices)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(cardServices);

        _videoList = Lifetime.Own(new VideoListView(viewModel, cardServices));
        subscriptions_video_list_host.Append(_videoList.Widget);

        _channelBar = Lifetime.Own(new SubscriptionsChannelBarView(
            viewModel, cardServices.Thumbnails, cardServices.Navigation, () => _videoList.GetBatchSize()));
        subscriptions_channel_bar_host.Append(_channelBar.Widget);

        Lifetime.Track(
            () => _videoList.RefreshLoadingChanged += OnVideoListRefreshLoadingChanged,
            () => _videoList.RefreshLoadingChanged -= OnVideoListRefreshLoadingChanged);

        ViewModel = viewModel;
    }

    public bool IsLoading => (ViewModel?.State.IsLoading ?? false) || _videoList.IsLoading;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public int GetBatchSize() => _videoList.GetBatchSize();

    public Task RefreshAsync() => _videoList.RefreshAsync();

    protected override void BindViewModel(SubscriptionsViewModel viewModel, BindingScope<SubscriptionsViewModel> bindings)
    {
        _channelBar.ViewModel = viewModel;
        bindings.Bind(
            nameof(SubscriptionsViewModel.State),
            this,
            static vm => vm.State,
            static (view, state) => view.RefreshLoadingChanged?.Invoke(view, state.IsLoading || state.IsLoadingMore));
    }

    private void OnVideoListRefreshLoadingChanged(object? sender, bool isLoading)
    {
        RefreshLoadingChanged?.Invoke(this, isLoading);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) RefreshLoadingChanged = null;
        base.Dispose(disposing);
    }
}
