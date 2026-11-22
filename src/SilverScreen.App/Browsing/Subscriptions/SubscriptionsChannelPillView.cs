using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsChannelPillView : ViewBase<Box>
{
    private const int AvatarSize = 56;
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsChannelPillView>();
    private readonly NativeMemoryPressure _memoryPressure = new();
    private readonly IThumbnailService _thumbnails;
    private Picture? _boundPicture;
    private Texture? _boundTexture;

    public SubscriptionsChannelPillView(
        SubscribedChannel channel, INavigationService navigation, IThumbnailService thumbnails,
        SemaphoreSlim avatarSemaphore, Action<SubscribedChannel> selectChannel,
        Action? focusPrevious, Action? focusNext, CancellationToken ct)
    {
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        var navigation1 = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        name_label.SetText(channel.Title);
        Widget.TooltipText = FormatChannelTooltip(channel);

        var leftClick = GestureClick.New();
        leftClick.Button = 1;
        Lifetime.Attach(Widget, leftClick, c => c.OnReleased += (_, _) => selectChannel(Channel),
            c => c.OnReleased -= (_, _) => selectChannel(Channel));

        var keyController = EventControllerKey.New();
        Lifetime.Attach(Widget, keyController, c => c.OnKeyPressed += (s, a) =>
        {
            switch (a.Keyval)
            {
                case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space:
                    selectChannel(Channel);
                    return true;
                case Constants.KEY_Left when focusPrevious != null:
                    focusPrevious();
                    return true;
                case Constants.KEY_Right when focusNext != null:
                    focusNext();
                    return true;
                default:
                    return false;
            }
        }, c => c.OnKeyPressed -= (_, _) => false);

        SubscriptionsChannelContextMenu.Attach(Widget, Channel, navigation1, Lifetime);
        if (!string.IsNullOrWhiteSpace(channel.AvatarUrl))
            LoadAvatarAsync(channel.AvatarUrl, avatarSemaphore, ct).FireAndForget(Logger);
    }

    public SubscribedChannel Channel { get; }

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

    private async Task LoadAvatarAsync(string avatarUrl, SemaphoreSlim semaphore, CancellationToken ct)
    {
        if (IsDisposed || ct.IsCancellationRequested) return;
        var acquired = false;
        try
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            var result = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (result is null || IsDisposed || ct.IsCancellationRequested) return;

            var pixbuf = await Task
                .Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true), ct)
                .ConfigureAwait(false);
            if (pixbuf is null || IsDisposed || ct.IsCancellationRequested)
            {
                pixbuf?.Dispose();
                return;
            }

            Functions.IdleAdd(0, () =>
            {
                try
                {
                    if (IsDisposed || ct.IsCancellationRequested) return false;
                    var texture = Texture.NewForPixbuf(pixbuf);
                    var picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = $"{Channel.Title} avatar";
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = picture.HeightRequest = AvatarSize;
                    picture.CanShrink = true;

                    avatar_overlay.Child = picture;
                    _boundTexture?.Dispose();
                    _boundPicture?.Dispose();
                    _boundTexture = texture;
                    _boundPicture = picture;
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

                return false;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load channel avatar for {Title}", Channel.Title);
        }
        finally
        {
            if (acquired)
                try
                {
                    semaphore.Release();
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
            _boundPicture?.Dispose();
            _boundPicture = null;
            _boundTexture?.Dispose();
            _boundTexture = null;
            _memoryPressure.Dispose();
        }

        base.Dispose(disposing);
    }
}