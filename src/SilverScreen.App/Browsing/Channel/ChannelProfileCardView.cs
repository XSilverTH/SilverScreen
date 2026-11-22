using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelProfileCardView(IThumbnailService thumbnails) : ViewBase<Box, ChannelViewModel>
{
    private const int AvatarSize = 80;
    private static readonly ILogger Logger = Log.ForContext<ChannelProfileCardView>();
    private readonly NativeMemoryPressure _avatarPressure = new();
    private readonly IThumbnailService _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
    private CancellationTokenSource? _avatarCancellation;
    private Picture? _boundPicture;
    private Texture? _boundTexture;

    protected override void BindViewModel(ChannelViewModel viewModel, BindingScope<ChannelViewModel> bindings)
    {
        bindings.Bind(nameof(ChannelViewModel.State), this, static vm => vm.State,
            static (view, state) => view.Render(state));
    }

    private void Render(ChannelViewState state)
    {
        channel_name.SetText(state.Name);
        var handle = ChannelFormatter.ExtractHandle(state.Url);
        channel_handle.Visible = !string.IsNullOrWhiteSpace(handle);
        if (!string.IsNullOrWhiteSpace(handle)) channel_handle.SetText(handle);
        channel_subscribers.Visible = state.SubscriberCount is not null;
        if (state.SubscriberCount is { } subs) channel_subscribers.SetText(ChannelFormatter.FormatSubscribers(subs));
        channel_meta_dot1.Visible = channel_handle.Visible && channel_subscribers.Visible;
        var hasDescription = !string.IsNullOrWhiteSpace(state.Description);
        channel_description_scroller.Visible = hasDescription;
        channel_description.Buffer?.SetText(state.Description ?? string.Empty, -1);
        if (!string.IsNullOrWhiteSpace(state.AvatarUrl)) LoadAvatarAsync(state.AvatarUrl).FireAndForget(Logger);
        else ClearAvatar();
    }

    private async Task LoadAvatarAsync(string avatarUrl)
    {
        if (IsDisposed) return;
        try
        {
            await _avatarCancellation?.CancelAsync()!;
        }
        catch (Exception)
        {
        }

        _avatarCancellation?.Dispose();
        var ct = (_avatarCancellation = new CancellationTokenSource()).Token;

        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (result is null) return;
            pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, AvatarSize, AvatarSize, true), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load channel avatar");
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Avatar decode returned no pixbuf.");
        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested) return false;
                var texture = Texture.NewForPixbuf(decodedPixbuf);
                if (_boundPicture is null)
                {
                    _boundPicture = Picture.NewForPaintable(texture);
                    _boundPicture.AlternativeText = "Channel avatar";
                    _boundPicture.ContentFit = ContentFit.Cover;
                    _boundPicture.HeightRequest = _boundPicture.WidthRequest = AvatarSize;
                    _boundPicture.CanShrink = true;
                }
                else
                {
                    _boundPicture.Paintable = texture;
                }

                channel_avatar_overlay.Child = _boundPicture;
                _boundTexture?.Dispose();
                _boundTexture = texture;
                _avatarPressure.Track((long)AvatarSize * AvatarSize * 4);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to render channel avatar texture");
            }
            finally
            {
                decodedPixbuf.Dispose();
            }

            return false;
        });
    }

    private void ClearAvatar()
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
        var texture = _boundTexture;
        _boundTexture = null;
        _avatarPressure.Release();
        channel_avatar_overlay.Child = channel_avatar_placeholder;
        _boundPicture?.Paintable = null!;
        texture?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearAvatar();
            _avatarPressure.Dispose();
        }

        base.Dispose(disposing);
    }
}