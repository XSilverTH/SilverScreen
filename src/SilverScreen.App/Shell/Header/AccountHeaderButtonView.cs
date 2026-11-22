using System.ComponentModel;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Profile;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell.Header;

/// <summary>Encapsulates the account header button, status indicator, avatar, and popover host.</summary>
public partial class AccountHeaderButtonView : ViewBase<MenuButton>
{
    private static readonly ILogger Logger = Log.ForContext<AccountHeaderButtonView>();
    private readonly NativeMemoryPressure _avatarPressure = new();
    private readonly IThumbnailService _thumbnails;
    private readonly AccountViewModel _viewModel;
    private CancellationTokenSource? _avatarCancellation;
    private Texture? _avatarTexture;
    private string? _loadedAvatarUrl;

    public AccountHeaderButtonView(
        AccountViewModel viewModel, IThumbnailService thumbnails,
        INativeAuthService authService, INotificationService notifications)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        var popover = Lifetime.Own(new AccountPopoverView(thumbnails, authService, notifications));
        popover.ViewModel = viewModel;
        account_popover.Child = popover.Widget;

        Lifetime.Track(
            () => _viewModel.PropertyChanged += OnPropertyChanged,
            () => _viewModel.PropertyChanged -= OnPropertyChanged);
        Render();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Lifetime.Idle(() =>
        {
            if (!IsDisposed) Render();
            return false;
        });
    }

    private void Render()
    {
        var hasManual = _viewModel.HasManualSession;
        var status = !hasManual ? AccountProfilePresentationStatus.SignedOut : _viewModel.ProfileStatus;
        account_status_stack.VisibleChildName = status switch
        {
            AccountProfilePresentationStatus.Ready => "avatar",
            AccountProfilePresentationStatus.Checking => "checking",
            AccountProfilePresentationStatus.Rejected or AccountProfilePresentationStatus.Unavailable => "warning",
            _ => "signed_out"
        };
        Widget.TooltipText = status switch
        {
            AccountProfilePresentationStatus.Ready => $"YouTube account: {_viewModel.DisplayName}",
            AccountProfilePresentationStatus.Checking => "Checking YouTube account",
            AccountProfilePresentationStatus.Rejected => "YouTube session expired — sign in again",
            AccountProfilePresentationStatus.Unavailable => "Couldn’t verify YouTube account — retry",
            _ => hasManual ? "YouTube account" : "Sign in to YouTube"
        };
        var isReady = status == AccountProfilePresentationStatus.Ready;
        account_avatar.Text = isReady ? _viewModel.DisplayName : string.Empty;
        account_avatar.ShowInitials = isReady;
        if (isReady && !string.IsNullOrWhiteSpace(_viewModel.AvatarUrl))
            LoadAvatarAsync(_viewModel.AvatarUrl).FireAndForget(Logger);
        else ClearAvatar();
    }

    private async Task LoadAvatarAsync(string avatarUrl)
    {
        if (string.Equals(_loadedAvatarUrl, avatarUrl, StringComparison.Ordinal)) return;
        _loadedAvatarUrl = avatarUrl;
        try
        {
            await _avatarCancellation?.CancelAsync()!;
        }
        catch (Exception)
        {
        }

        _avatarCancellation?.Dispose();
        var ct = (_avatarCancellation = new CancellationTokenSource()).Token;
        Pixbuf? pixbuf = null;
        try
        {
            var thumbnail = await _thumbnails.GetThumbnailAsync(avatarUrl, ct).ConfigureAwait(false);
            if (thumbnail is not null)
                pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(thumbnail.LocalPath, 48, 48, true), ct)
                    .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException) Logger.Warning(ex, "Failed avatar load");
            return;
        }

        if (pixbuf is null || IsDisposed || ct.IsCancellationRequested)
        {
            pixbuf?.Dispose();
            return;
        }

        var decoded = pixbuf;
        Lifetime.Idle(() =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested) return false;
                var texture = Texture.NewForPixbuf(decoded);
                account_avatar.CustomImage = texture;
                _avatarTexture?.Dispose();
                _avatarTexture = texture;
                _avatarPressure.Track(48 * 48 * 4);
            }
            finally
            {
                decoded.Dispose();
            }

            return false;
        });
    }

    private void ClearAvatar()
    {
        _loadedAvatarUrl = null;
        try
        {
            _avatarCancellation?.Cancel();
        }
        catch (Exception)
        {
        }

        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
        account_avatar.CustomImage = null!;
        _avatarTexture?.Dispose();
        _avatarTexture = null;
        _avatarPressure.Release();
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