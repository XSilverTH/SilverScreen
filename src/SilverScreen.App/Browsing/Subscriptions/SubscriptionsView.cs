using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Pango;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using Action = System.Action;
using Rectangle = Gdk.Rectangle;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsView : ViewBase<Box>
{
    private const int AvatarSize = 56;
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsView>();

    private readonly List<ChannelItemHolder> _channelItems = [];
    private readonly Action<string, string> _openChannel;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoListView _videoList;
    private readonly SubscriptionsViewModel _viewModel;

    private CancellationTokenSource? _avatarsCancellation;
    private bool _disposed;
    private IReadOnlyList<SubscribedChannel> _renderedChannels = [];

    public SubscriptionsView(
        SubscriptionsViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions,
        Action openWebLogin,
        Action<string, string> openChannel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _openChannel = openChannel ?? throw new ArgumentNullException(nameof(openChannel));

        _videoList = new VideoListView(
            viewModel,
            thumbnails,
            watchProgress ?? throw new ArgumentNullException(nameof(watchProgress)),
            videoActions ?? throw new ArgumentNullException(nameof(videoActions)),
            openWebLogin);
        _videoList.RefreshLoadingChanged += OnVideoListRefreshLoadingChanged;
        subscriptions_video_list_host.Append(_videoList.Widget);

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State.IsLoading || _videoList.IsLoading;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public int GetBatchSize()
    {
        return _videoList.GetBatchSize();
    }

    public async Task RefreshAsync()
    {
        await _viewModel.RefreshAsync(GetBatchSize()).ConfigureAwait(false);
    }

    private void OnVideoListRefreshLoadingChanged(object? sender, bool isLoading)
    {
        RefreshLoadingChanged?.Invoke(this, isLoading);
    }

    private void OnStateChanged(object? sender, SubscriptionsViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed)
                return false;

            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            Render(state);
            return false;
        });
    }

    private void Render(SubscriptionsViewState state)
    {
        if (_disposed) return;

        // Update "All" toggle button state
        if (state.SelectedChannel is null)
            all_channel_button.AddCssClass("active");
        else
            all_channel_button.RemoveCssClass("active");

        // Reveal channel bar only when we have channels
        channel_bar_revealer.RevealChild = state.Channels.Count > 0;

        // If channels collection changed, rebuild channel items
        if (!AreChannelsEqual(_renderedChannels, state.Channels))
        {
            RebuildChannelItems(state.Channels);
            _renderedChannels = state.Channels;
        }

        // Update selection highlight on channel items
        foreach (var holder in _channelItems)
        {
            var isSelected = state.SelectedChannel is { } selected &&
                             (selected.Id == holder.Channel.Id || selected.Url == holder.Channel.Url);

            if (isSelected)
                holder.ItemBox.AddCssClass("selected");
            else
                holder.ItemBox.RemoveCssClass("selected");
        }
    }

    private static bool AreChannelsEqual(IReadOnlyList<SubscribedChannel> a, IReadOnlyList<SubscribedChannel> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        return !a.Where((t, i) =>
            t.Id != b[i].Id || t.Url != b[i].Url || t.Title != b[i].Title || t.AvatarUrl != b[i].AvatarUrl).Any();
    }

    private void RebuildChannelItems(IReadOnlyList<SubscribedChannel> channels)
    {
        ClearChannelItems();

        _avatarsCancellation?.Cancel();
        _avatarsCancellation?.Dispose();
        _avatarsCancellation = new CancellationTokenSource();
        var cancellationToken = _avatarsCancellation.Token;

        foreach (var channel in channels)
        {
            var holder = CreateChannelItem(channel, cancellationToken);
            _channelItems.Add(holder);
            channel_bar_box.Append(holder.ItemBox);
        }
    }

    private ChannelItemHolder CreateChannelItem(SubscribedChannel channel, CancellationToken cancellationToken)
    {
        var itemBox = Box.New(Orientation.Vertical, 4);
        itemBox.Halign = Align.Center;
        itemBox.Valign = Align.Center;
        itemBox.AddCssClass("subscriptions-channel-item");
        itemBox.TooltipText = channel.Title;

        var avatarOverlay = Overlay.New();
        avatarOverlay.WidthRequest = AvatarSize;
        avatarOverlay.HeightRequest = AvatarSize;
        avatarOverlay.Halign = Align.Center;
        avatarOverlay.Valign = Align.Center;
        avatarOverlay.AddCssClass("subscriptions-channel-avatar");

        var placeholder = Image.NewFromIconName("avatar-default-symbolic");
        placeholder.PixelSize = 32;
        placeholder.Halign = Align.Center;
        placeholder.Valign = Align.Center;
        avatarOverlay.Child = placeholder;

        var nameLabel = Label.New(channel.Title);
        nameLabel.Halign = Align.Center;
        nameLabel.Xalign = 0.5f;
        nameLabel.Ellipsize = EllipsizeMode.End;
        nameLabel.Lines = 1;
        nameLabel.MaxWidthChars = 11;
        nameLabel.AddCssClass("subscriptions-channel-title");

        itemBox.Append(avatarOverlay);
        itemBox.Append(nameLabel);

        // Primary click: Filter by this channel
        var leftClick = GestureClick.New();
        leftClick.Button = 1;
        leftClick.OnReleased += (_, _) =>
        {
            _viewModel.SelectChannelAsync(channel, _videoList.GetBatchSize()).FireAndForget(Logger);
        };
        itemBox.AddController(leftClick);

        // Secondary / context click: Go to channel page
        var menu = Menu.New();
        menu.Append("Go to Channel page", "channel-item.open-channel");

        var actionGroup = SimpleActionGroup.New();
        var openAction = SimpleAction.New("open-channel", null);
        openAction.OnActivate += (_, _) => { _openChannel(channel.Url, channel.Title); };
        actionGroup.AddAction(openAction);

        var popover = PopoverMenu.NewFromModel(menu);
        popover.SetParent(itemBox);
        popover.HasArrow = false;
        popover.InsertActionGroup("channel-item", actionGroup);

        var rightClick = GestureClick.New();
        rightClick.Button = 3;
        rightClick.OnPressed += (sender, args) =>
        {
            sender.SetState(EventSequenceState.Claimed);
            var rect = new Rectangle
            {
                X = (int)args.X,
                Y = (int)args.Y,
                Width = 1,
                Height = 1
            };
            popover.SetPointingTo(rect);
            popover.Popup();
        };
        itemBox.AddController(rightClick);

        var holder = new ChannelItemHolder(channel, itemBox, avatarOverlay, popover);

        // Asynchronously load circular avatar
        if (!string.IsNullOrWhiteSpace(channel.AvatarUrl))
            LoadAvatarAsync(holder, channel.AvatarUrl, cancellationToken).FireAndForget(Logger);

        return holder;
    }

    private async Task LoadAvatarAsync(ChannelItemHolder holder, string avatarUrl, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(avatarUrl, cancellationToken).ConfigureAwait(false);
            if (result is null) return;

            pixbuf = await Task.Run(
                () => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load channel avatar from {AvatarUrl}", avatarUrl);
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Avatar decode returned no pixbuf.");

        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                    return false;

                Texture? texture = null;
                Picture? picture = null;
                try
                {
                    var pixbufForTexture = decodedPixbuf ??
                                           throw new InvalidOperationException(
                                               "Avatar decode was released before texture creation.");
                    texture = Texture.NewForPixbuf(pixbufForTexture);
                    pixbufForTexture.Dispose();
                    decodedPixbuf = null;

                    picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = $"{holder.Channel.Title} avatar";
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = AvatarSize;
                    picture.HeightRequest = AvatarSize;
                    picture.CanShrink = true;

                    holder.Overlay.Child = picture;
                    holder.BoundTexture?.Dispose();
                    holder.BoundPicture?.Dispose();
                    holder.BoundTexture = texture;
                    holder.BoundPicture = picture;
                    texture = null;
                    picture = null;
                }
                finally
                {
                    picture?.Dispose();
                    texture?.Dispose();
                }
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to render channel avatar texture from {AvatarUrl}", avatarUrl);
            }
            finally
            {
                decodedPixbuf?.Dispose();
            }

            return false;
        });
    }

    private void ClearChannelItems()
    {
        foreach (var holder in _channelItems)
        {
            channel_bar_box.Remove(holder.ItemBox);
            holder.Dispose();
        }

        _channelItems.Clear();
    }

    private void OnAllButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _viewModel.SelectChannelAsync(null, _videoList.GetBatchSize()).FireAndForget(Logger);
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _viewModel.StateChanged -= OnStateChanged;
        _videoList.RefreshLoadingChanged -= OnVideoListRefreshLoadingChanged;
        _avatarsCancellation?.Cancel();
        _avatarsCancellation?.Dispose();
        _avatarsCancellation = null;

        ClearChannelItems();
        _videoList.Dispose();
        base.Dispose();
    }

    private sealed class ChannelItemHolder(
        SubscribedChannel channel,
        Box itemBox,
        Overlay overlay,
        PopoverMenu popover) : IDisposable
    {
        public SubscribedChannel Channel { get; } = channel;
        public Box ItemBox { get; } = itemBox;
        public Overlay Overlay { get; } = overlay;
        private PopoverMenu Popover { get; } = popover;
        public Texture? BoundTexture { get; set; }
        public Picture? BoundPicture { get; set; }

        public void Dispose()
        {
            BoundPicture?.Dispose();
            BoundPicture = null;
            BoundTexture?.Dispose();
            BoundTexture = null;
            Popover.Dispose();
            Overlay.Dispose();
            ItemBox.Dispose();
        }
    }
}