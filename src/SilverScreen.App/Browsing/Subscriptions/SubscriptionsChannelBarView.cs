using GObject;
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
    private readonly StringList _channelIds;
    private readonly Dictionary<string, int> _channelIndicesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SubscribedChannel> _channelsByKey = new(StringComparer.Ordinal);
    private readonly SubscriptionsChannelContextMenu _contextMenu;
    private readonly SignalListItemFactory _factory;
    private readonly Func<int> _getBatchSize;
    private readonly Dictionary<Widget, SubscriptionsChannelPillView> _pillsByCell = [];
    private readonly IThumbnailService _thumbnails;
    private string[] _displayedChannelKeys = [];
    private bool? _lastAllSelected;
    private IReadOnlyList<SubscribedChannel> _renderedChannels = [];
    private SubscribedChannel? _selectedChannel;

    public SubscriptionsChannelBarView(
        SubscriptionsViewModel viewModel, IThumbnailService thumbnails, INavigationService navigation,
        Func<int>? getBatchSize = null)
    {
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        ArgumentNullException.ThrowIfNull(navigation);
        _getBatchSize = getBatchSize ?? (() => 20);
        _contextMenu = Lifetime.Own(new SubscriptionsChannelContextMenu(navigation));

        var allKeyController = EventControllerKey.New();
        ReturningSignalHandler<EventControllerKey, EventControllerKey.KeyPressedSignalArgs, bool> allKeyPressedHandler =
            (_, e) => e.Keyval == Constants.KEY_Right && _displayedChannelKeys.Length > 0 &&
                      channel_bar_list.GrabFocus();
        Lifetime.Attach(all_channel_button, allKeyController,
            c => c.OnKeyPressed += allKeyPressedHandler,
            c => c.OnKeyPressed -= allKeyPressedHandler);

        _channelIds = Lifetime.Own(StringList.New([]));
        var selection = Lifetime.Own(NoSelection.New(_channelIds));
        _factory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(() => _factory.OnSetup += OnPillSetup, () => _factory.OnSetup -= OnPillSetup);
        Lifetime.Track(() => _factory.OnBind += OnPillBind, () => _factory.OnBind -= OnPillBind);
        Lifetime.Track(() => _factory.OnUnbind += OnPillUnbind, () => _factory.OnUnbind -= OnPillUnbind);
        Lifetime.Track(() => _factory.OnTeardown += OnPillTeardown, () => _factory.OnTeardown -= OnPillTeardown);

        channel_bar_list.Model = selection;
        Lifetime.Track(
            () => channel_bar_list.Factory = _factory,
            () => channel_bar_list.Factory = null);

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

        var isAllSelected = state.SelectedChannel is null;
        if (_lastAllSelected != isAllSelected)
        {
            _lastAllSelected = isAllSelected;
            if (isAllSelected)
            {
                all_channel_button.AddCssClass("active");
                all_channel_button.AddCssClass("selected");
            }
            else
            {
                all_channel_button.RemoveCssClass("active");
                all_channel_button.RemoveCssClass("selected");
            }
        }

        Widget.RevealChild = state.Channels.Count > 0;

        var channelsChanged = !AreChannelsEqual(_renderedChannels, state.Channels);
        if (channelsChanged)
        {
            ApplyChannels(state.Channels);
            _renderedChannels = state.Channels;
        }

        var selectionChanged = !Equals(_selectedChannel, state.SelectedChannel);
        _selectedChannel = state.SelectedChannel;

        if (channelsChanged || selectionChanged)
            foreach (var pill in _pillsByCell.Values)
                if (pill.Channel is { } ch)
                    pill.SetSelected(IsChannelSelected(ch, _selectedChannel));
    }

    private void ApplyChannels(IReadOnlyList<SubscribedChannel> channels)
    {
        var nextKeys = new string[channels.Count];
        for (var i = 0; i < channels.Count; i++)
            nextKeys[i] = GetChannelKey(channels[i]);

        var prefixLength = 0;
        while (prefixLength < _displayedChannelKeys.Length && prefixLength < nextKeys.Length &&
               string.Equals(_displayedChannelKeys[prefixLength], nextKeys[prefixLength], StringComparison.Ordinal))
            prefixLength++;

        var suffixLength = 0;
        while (_displayedChannelKeys.Length - suffixLength > prefixLength &&
               nextKeys.Length - suffixLength > prefixLength &&
               string.Equals(_displayedChannelKeys[_displayedChannelKeys.Length - suffixLength - 1],
                   nextKeys[nextKeys.Length - suffixLength - 1], StringComparison.Ordinal))
            suffixLength++;

        var removedMiddleCount = _displayedChannelKeys.Length - prefixLength - suffixLength;
        var addedMiddleCount = nextKeys.Length - prefixLength - suffixLength;

        _channelsByKey.Clear();
        _channelIndicesByKey.Clear();
        for (var i = 0; i < channels.Count; i++)
        {
            var key = nextKeys[i];
            _channelsByKey[key] = channels[i];
            _channelIndicesByKey[key] = i;
        }

        _displayedChannelKeys = nextKeys;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedMiddleKeys = new string[addedMiddleCount];
        for (var i = 0; i < addedMiddleCount; i++)
            addedMiddleKeys[i] = nextKeys[prefixLength + i];

        _channelIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleKeys);
    }

    private void OnPillSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        listItem.Focusable = true;
        var pill = new SubscriptionsChannelPillView(
            _thumbnails,
            _avatarSemaphore,
            c => ViewModel?.SelectChannelAsync(c, _getBatchSize()).FireAndForget(Logger),
            _contextMenu,
            () => all_channel_button.GrabFocus());
        listItem.Child = pill.Widget;
        _pillsByCell[pill.Widget] = pill;
    }

    private void OnPillBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject { String: { } key } } ||
            !_pillsByCell.TryGetValue(child, out var pill) ||
            !_channelsByKey.TryGetValue(key, out var channel))
            return;

        var index = _channelIndicesByKey.GetValueOrDefault(key, -1);
        pill.Bind(channel, index, IsChannelSelected(channel, _selectedChannel));
    }

    private void OnPillUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem { Child: { } child } && _pillsByCell.TryGetValue(child, out var pill))
            pill.Unbind();
    }

    private void OnPillTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        if (listItem.Child is { } child && _pillsByCell.Remove(child, out var pill))
        {
            pill.Unbind();
            pill.Dispose();
        }

        listItem.Child = null;
    }

    private void OnAllButtonClicked(object? sender = null, EventArgs? args = null)
    {
        ViewModel?.SelectChannelAsync(null, _getBatchSize()).FireAndForget(Logger);
    }

    private static string GetChannelKey(SubscribedChannel channel)
    {
        return !string.IsNullOrWhiteSpace(channel.Id) ? channel.Id : channel.Url;
    }

    private static bool IsChannelSelected(SubscribedChannel channel, SubscribedChannel? selected)
    {
        return selected is not null && (selected.Id == channel.Id || selected.Url == channel.Url);
    }

    private static bool AreChannelsEqual(IReadOnlyList<SubscribedChannel> a, IReadOnlyList<SubscribedChannel> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Id != b[i].Id || a[i].Url != b[i].Url || a[i].Title != b[i].Title ||
                a[i].AvatarUrl != b[i].AvatarUrl)
                return false;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pill in _pillsByCell.Values)
                pill.Dispose();
            _pillsByCell.Clear();
            _channelsByKey.Clear();
            _channelIndicesByKey.Clear();

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
