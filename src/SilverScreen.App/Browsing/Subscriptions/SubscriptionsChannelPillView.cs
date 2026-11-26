using Gdk;
using GdkPixbuf;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsChannelPillView : ViewBase<Box>
{
    private const int AvatarSize = 56;
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsChannelPillView>();
    private readonly SemaphoreSlim _avatarSemaphore;
    private readonly SubscriptionsChannelContextMenu _contextMenu;
    private readonly Action? _focusAllButton;
    private readonly NativeMemoryPressure _memoryPressure = new();
    private readonly Action<SubscribedChannel> _selectChannel;
    private readonly IThumbnailService _thumbnails;
    private CancellationTokenSource? _avatarCancellation;
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private int _index = -1;
    private string? _loadedAvatarUrl;

    public SubscriptionsChannelPillView(
        IThumbnailService thumbnails,
        SemaphoreSlim avatarSemaphore,
        Action<SubscribedChannel> selectChannel,
        SubscriptionsChannelContextMenu contextMenu,
        Action? focusAllButton = null)
    {
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _avatarSemaphore = avatarSemaphore ?? throw new ArgumentNullException(nameof(avatarSemaphore));
        _selectChannel = selectChannel ?? throw new ArgumentNullException(nameof(selectChannel));
        _contextMenu = contextMenu ?? throw new ArgumentNullException(nameof(contextMenu));
        _focusAllButton = focusAllButton;

        var leftClick = GestureClick.New();
        leftClick.Button = 1;
        SignalHandler<GestureClick, GestureClick.ReleasedSignalArgs> leftReleasedHandler = (_, _) =>
        {
            if (Channel is { } channel)
                _selectChannel(channel);
        };
        Lifetime.Attach(Widget, leftClick,
            c => c.OnReleased += leftReleasedHandler,
            c => c.OnReleased -= leftReleasedHandler);

        var rightClick = GestureClick.New();
        rightClick.Button = 3;
        SignalHandler<GestureClick, GestureClick.PressedSignalArgs> rightPressedHandler = (s, a) =>
        {
            if (Channel is not { } channel) return;
            s.SetState(EventSequenceState.Claimed);
            _contextMenu.ShowFor(Widget, channel, a.X, a.Y);
        };
        Lifetime.Attach(Widget, rightClick,
            c => c.OnPressed += rightPressedHandler,
            c => c.OnPressed -= rightPressedHandler);

        var keyController = EventControllerKey.New();
        ReturningSignalHandler<EventControllerKey, EventControllerKey.KeyPressedSignalArgs, bool> keyPressedHandler =
            (_, a) =>
            {
                if (Channel is not { } channel) return false;
                switch (a.Keyval)
                {
                    case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space:
                        _selectChannel(channel);
                        return true;
                    case Constants.KEY_Left when _index == 0 && _focusAllButton is not null:
                        _focusAllButton();
                        return true;
                    default:
                        return false;
                }
            };
        Lifetime.Attach(Widget, keyController,
            c => c.OnKeyPressed += keyPressedHandler,
            c => c.OnKeyPressed -= keyPressedHandler);
    }

    public SubscribedChannel? Channel { get; private set; }

    public void Bind(SubscribedChannel channel, int index, bool isSelected)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(channel);

        _index = index;
        var sameChannel = Channel is { } prev &&
                          string.Equals(prev.Id, channel.Id, StringComparison.Ordinal) &&
                          string.Equals(prev.Title, channel.Title, StringComparison.Ordinal) &&
                          string.Equals(prev.Url, channel.Url, StringComparison.Ordinal);

        Channel = channel;
        if (!sameChannel)
        {
            name_label.SetText(channel.Title);
            Widget.TooltipText = FormatChannelTooltip(channel);
        }

        SetSelected(isSelected);

        var avatarUrl = channel.AvatarUrl;
        var sameAvatarUrl = !string.IsNullOrWhiteSpace(avatarUrl) &&
                            string.Equals(_loadedAvatarUrl, avatarUrl, StringComparison.Ordinal) &&
                            (_boundTexture is not null || _avatarCancellation is not null);
        if (sameAvatarUrl)
        {
            if (_boundPicture is not null)
                _boundPicture.AlternativeText = $"{channel.Title} avatar";
            return;
        }

        CancelAvatarLoad();
        ClearAvatar();
        _loadedAvatarUrl = avatarUrl;

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            var generation = ++_bindingGeneration;
            _avatarCancellation = new CancellationTokenSource();
            LoadAvatarAsync(avatarUrl, channel.Title, generation, _avatarCancellation.Token).FireAndForget(Logger);
        }
    }

    public void Unbind()
    {
        _bindingGeneration++;
        _index = -1;
        Channel = null;
        _loadedAvatarUrl = null;
        _contextMenu.DetachIfAttached(Widget);
        CancelAvatarLoad();
        ClearAvatar();
        SetSelected(false);
        name_label.SetText(string.Empty);
        Widget.TooltipText = string.Empty;
    }

    public void SetSelected(bool isSelected)
    {
        if (isSelected)
        {
            Widget.AddCssClass("selected");
            Widget.AddCssClass("active");
        }
        else
        {
            Widget.RemoveCssClass("selected");
            Widget.RemoveCssClass("active");
        }
    }

    private void CancelAvatarLoad()
    {
        try
        {
            _avatarCancellation?.Cancel();
        }
        catch (Exception)
        {
        }

        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
    }

    private void ClearAvatar()
    {
        var texture = _boundTexture;
        _boundTexture = null;
        _memoryPressure.Release();
        avatar_overlay.Child = placeholder_avatar;
        if (_boundPicture is not null)
            _boundPicture.Paintable = null!;
        texture?.Dispose();
    }

    private async Task LoadAvatarAsync(string avatarUrl, string title, int generation, CancellationToken ct)
    {
        if (IsDisposed || ct.IsCancellationRequested) return;
        var acquired = false;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (result is null || IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation) return;

            await _avatarSemaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation) return;

            var pixbuf = await Task
                .Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true), ct)
                .ConfigureAwait(false);
            if (pixbuf is null) return;

            GtkUiDispatch.Run(() =>
            {
                try
                {
                    if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation) return;
                    var texture = Texture.NewForPixbuf(pixbuf);
                    if (_boundPicture is null)
                    {
                        var picture = Picture.NewForPaintable(texture);
                        picture.ContentFit = ContentFit.Cover;
                        picture.WidthRequest = picture.HeightRequest = AvatarSize;
                        picture.CanShrink = true;
                        _boundPicture = picture;
                    }
                    else
                    {
                        _boundPicture.Paintable = texture;
                    }

                    _boundPicture.AlternativeText = $"{title} avatar";
                    avatar_overlay.Child = _boundPicture;
                    _boundTexture?.Dispose();
                    _boundTexture = texture;
                    _memoryPressure.Track(AvatarSize * AvatarSize * 4);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to render channel avatar texture");
                }
                finally
                {
                    pixbuf.Dispose();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load channel avatar for {Title}", title);
        }
        finally
        {
            if (acquired)
                try
                {
                    _avatarSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                }
        }
    }

    private static string FormatChannelTooltip(SubscribedChannel ch)
    {
        return ChannelFormatter.ExtractHandle(ch.Url) is { Length: > 0 } h &&
               !string.Equals(ch.Title, h, StringComparison.OrdinalIgnoreCase)
            ? $"{ch.Title} ({h})"
            : ch.Title;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            _boundPicture?.Dispose();
            _boundPicture = null;
            _memoryPressure.Dispose();
        }

        base.Dispose(disposing);
    }
}