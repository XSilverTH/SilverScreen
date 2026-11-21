using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Subscriptions;

public partial class SubscriptionsChannelPillView : ViewBase<Box>
{
    private const int AvatarSize = 56;
    private static readonly ILogger Logger = Log.ForContext<SubscriptionsChannelPillView>();
    private readonly SubscribedChannel _channel;
    private readonly INavigationService _navigation;
    private readonly IThumbnailService _thumbnails;
    private readonly NativeMemoryPressure _memoryPressure = new();
    private Texture? _boundTexture; private Picture? _boundPicture;

    public SubscriptionsChannelPillView(
        SubscribedChannel channel, INavigationService navigation, IThumbnailService thumbnails,
        SemaphoreSlim avatarSemaphore, Action<SubscribedChannel> selectChannel,
        Action? focusPrevious, Action? focusNext, CancellationToken ct)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        name_label.SetText(channel.Title);
        Widget.TooltipText = FormatChannelTooltip(channel);

        var leftClick = GestureClick.New(); leftClick.Button = 1;
        Lifetime.Attach(Widget, leftClick, c => c.OnReleased += (_, _) => selectChannel(_channel), c => c.OnReleased -= (_, _) => selectChannel(_channel));

        var keyController = EventControllerKey.New();
        Lifetime.Attach(Widget, keyController,
            c => c.OnKeyPressed += (s, a) =>
            {
                if (a.Keyval is Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space) { selectChannel(_channel); return true; }
                if (a.Keyval == Constants.KEY_Left && focusPrevious != null) { focusPrevious(); return true; }
                if (a.Keyval == Constants.KEY_Right && focusNext != null) { focusNext(); return true; }
                return false;
            },
            c => c.OnKeyPressed -= (_, _) => false);

        SubscriptionsChannelContextMenu.Attach(Widget, _channel, _navigation, Lifetime);
        if (!string.IsNullOrWhiteSpace(channel.AvatarUrl)) LoadAvatarAsync(channel.AvatarUrl, avatarSemaphore, ct).FireAndForget(Logger);
    }

    public SubscribedChannel Channel => _channel;

    public void SetSelected(bool isSelected)
    {
        if (isSelected) { Widget.AddCssClass("selected"); Widget.AddCssClass("active"); }
        else { Widget.RemoveCssClass("selected"); Widget.RemoveCssClass("active"); }
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

            using var pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true), ct).ConfigureAwait(false);
            if (pixbuf is null || IsDisposed || ct.IsCancellationRequested) return;

            Functions.IdleAdd(0, () =>
            {
                if (IsDisposed || ct.IsCancellationRequested) return false;
                var texture = Texture.NewForPixbuf(pixbuf);
                var picture = Picture.NewForPaintable(texture);
                picture.AlternativeText = $"{_channel.Title} avatar";
                picture.ContentFit = ContentFit.Cover;
                picture.WidthRequest = picture.HeightRequest = AvatarSize;
                picture.CanShrink = true;

                avatar_overlay.Child = picture;
                _boundTexture?.Dispose(); _boundPicture?.Dispose();
                _boundTexture = texture; _boundPicture = picture;
                _memoryPressure.Track(AvatarSize * AvatarSize * 4);
                return false;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logger.Warning(ex, "Failed to load channel avatar for {Title}", _channel.Title); }
        finally { if (acquired) try { semaphore.Release(); } catch (ObjectDisposedException) { } }
    }

    private static string FormatChannelTooltip(SubscribedChannel ch)
    {
        var h = ChannelFormatter.ExtractHandle(ch.Url);
        return !string.IsNullOrWhiteSpace(h) && !string.Equals(ch.Title, h, StringComparison.OrdinalIgnoreCase) ? $"{ch.Title} ({h})" : ch.Title;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _boundPicture?.Dispose(); _boundPicture = null; _boundTexture?.Dispose(); _boundTexture = null; _memoryPressure.Dispose(); }
        base.Dispose(disposing);
    }
}
