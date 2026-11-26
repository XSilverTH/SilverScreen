using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Account.Profile;

public partial class AccountSignedInCardView(IThumbnailService thumbnails) : ViewBase<Box, AccountViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<AccountSignedInCardView>();
    private readonly NativeMemoryPressure _avatarPressure = new();
    private readonly IThumbnailService _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
    private CancellationTokenSource? _avatarCancellation;
    private Texture? _avatarTexture;
    private string? _avatarUrl;

    protected override void BindViewModel(AccountViewModel viewModel, BindingScope<AccountViewModel> bindings)
    {
        bindings.Bind(nameof(AccountViewModel.DisplayName), this,
            static vm => vm.DisplayName,
            static (view, name) =>
            {
                view.signed_in_display_name.SetText(name);
                view.signed_in_avatar.Text = name;
            });

        bindings.Bind(nameof(AccountViewModel.AvatarUrl), this,
            static vm => vm.AvatarUrl,
            static (view, url) => view.UpdateAvatar(url));
    }

    private void UpdateAvatar(string? avatarUrl)
    {
        if (string.Equals(_avatarUrl, avatarUrl, StringComparison.Ordinal)) return;
        _avatarUrl = avatarUrl;
        ClearAvatar();
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;

        var cts = new CancellationTokenSource();
        _avatarCancellation = cts;
        LoadAvatarAsync(avatarUrl, cts.Token).FireAndForget(Logger);
    }

    private async Task LoadAvatarAsync(string avatarUrl, CancellationToken ct)
    {
        Pixbuf? pixbuf;
        try
        {
            var thumbnail = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (thumbnail is null) return;
            pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(thumbnail.LocalPath, 128, 128, true), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            Logger.Debug("Failed to load account avatar");
            return;
        }

        var decodedPixbuf = pixbuf;
        if (IsDisposed || ct.IsCancellationRequested || decodedPixbuf is null)
        {
            decodedPixbuf?.Dispose();
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested ||
                    !string.Equals(_avatarUrl, avatarUrl, StringComparison.Ordinal))
                    return false;

                var texture = Texture.NewForPixbuf(decodedPixbuf);
                signed_in_avatar.CustomImage = texture;
                _avatarTexture?.Dispose();
                _avatarTexture = texture;
                _avatarPressure.Track(128 * 128 * 4);
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
        signed_in_avatar.CustomImage = null!;
        _avatarTexture?.Dispose();
        _avatarTexture = null;
        _avatarPressure.Release();
    }

    private void OnSignOutClicked(Button sender, EventArgs args)
    {
        ViewModel?.ClearSessionAsync().FireAndForget(Logger);
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