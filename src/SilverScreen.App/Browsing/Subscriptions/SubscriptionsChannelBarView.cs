using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsChannelBarView : ViewBase<Revealer, SubscriptionsViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsChannelBarView>();
    private readonly SemaphoreSlim _avatarSemaphore = new(4, 4);
    private readonly Func<int> _getBatchSize;
    private readonly INavigationService _navigation;
    private readonly List<SubscriptionsChannelPillView> _pills = [];
    private readonly IThumbnailService _thumbnails;
    private CancellationTokenSource? _avatarsCancellation;
    private IReadOnlyList<SubscribedChannel> _renderedChannels = [];

    public SubscriptionsChannelBarView(
        SubscriptionsViewModel viewModel, IThumbnailService thumbnails, INavigationService navigation,
        Func<int>? getBatchSize = null)
    {
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _getBatchSize = getBatchSize ?? (() => 20);

        var allKeyController = EventControllerKey.New();
        Lifetime.Attach(all_channel_button, allKeyController,
            c => c.OnKeyPressed += (_, e) =>
                e.Keyval == Constants.KEY_Right && _pills.Count > 0 && (_pills[0].Widget.GrabFocus(), true).Item2,
            c => c.OnKeyPressed -= (_, _) => false);

        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    protected override void BindViewModel(SubscriptionsViewModel viewModel,
        BindingScope<SubscriptionsViewModel> bindings)
    {
        bindings.Bind(nameof(SubscriptionsViewModel.State), this, static vm => vm.State,
            static (view, state) => view.Render(state));
    }

    private void Render(SubscriptionsViewState state)
    {
        if (IsDisposed) return;
        if (state.SelectedChannel is null)
        {
            all_channel_button.AddCssClass("active");
            all_channel_button.AddCssClass("selected");
        }
        else
        {
            all_channel_button.RemoveCssClass("active");
            all_channel_button.RemoveCssClass("selected");
        }

        Widget.RevealChild = state.Channels.Count > 0;

        if (!AreChannelsEqual(_renderedChannels, state.Channels))
        {
            RebuildChannelPills(state.Channels);
            _renderedChannels = state.Channels;
        }

        foreach (var pill in _pills)
            pill.SetSelected(state.SelectedChannel is { } sel &&
                             (sel.Id == pill.Channel.Id || sel.Url == pill.Channel.Url));
    }

    private void RebuildChannelPills(IReadOnlyList<SubscribedChannel> channels)
    {
        ClearPills();
        try
        {
            _avatarsCancellation?.Cancel();
        }
        catch
        {
        }

        _avatarsCancellation?.Dispose();
        var ct = (_avatarsCancellation = new CancellationTokenSource()).Token;

        for (var i = 0; i < channels.Count; i++)
        {
            var (ch, prev, next) = (channels[i], i - 1, i + 1);
            Action focusPrev = prev < 0 ? () => all_channel_button.GrabFocus() : () => _pills[prev].Widget.GrabFocus();
            Action? focusNext = next < channels.Count ? () => _pills[next].Widget.GrabFocus() : null;

            var pill = new SubscriptionsChannelPillView(ch, _navigation, _thumbnails, _avatarSemaphore,
                c => ViewModel?.SelectChannelAsync(c, _getBatchSize()).FireAndForget(Logger), focusPrev, focusNext, ct);
            _pills.Add(pill);
            channel_bar_box.Append(pill.Widget);
        }
    }

    private void ClearPills()
    {
        foreach (var p in _pills)
        {
            try
            {
                channel_bar_box.Remove(p.Widget);
            }
            catch
            {
            }

            p.Dispose();
        }

        _pills.Clear();
    }

    private void OnAllButtonClicked(object? sender = null, EventArgs? args = null)
    {
        ViewModel?.SelectChannelAsync(null, _getBatchSize()).FireAndForget(Logger);
    }

    private static bool AreChannelsEqual(IReadOnlyList<SubscribedChannel> a, IReadOnlyList<SubscribedChannel> b)
    {
        return ReferenceEquals(a, b) || (a.Count == b.Count && !a.Where((t, i) =>
            t.Id != b[i].Id || t.Url != b[i].Url || t.Title != b[i].Title || t.AvatarUrl != b[i].AvatarUrl).Any());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _avatarsCancellation?.Cancel();
            }
            catch
            {
            }

            _avatarsCancellation?.Dispose();
            _avatarsCancellation = null;
            ClearPills();
            try
            {
                _avatarSemaphore.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        base.Dispose(disposing);
    }
}