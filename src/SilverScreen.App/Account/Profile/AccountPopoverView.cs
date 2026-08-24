using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Account.Profile;

public partial class AccountPopoverView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<AccountPopoverView>();
    private readonly Action _openWebLogin;
    private readonly Action<bool, string, Texture?> _sessionAppearanceChanged;
    private readonly IThumbnailService _thumbnails;
    private readonly AccountViewModel _viewModel;
    private CancellationTokenSource? _avatarCancellation;
    private Texture? _avatarTexture;
    private string? _avatarUrl;
    private bool _disposed;
    private bool _editing;

    public AccountPopoverView(
        AccountViewModel viewModel,
        IThumbnailService thumbnails,
        Action openWebLogin,
        Action<bool, string, Texture?> sessionAppearanceChanged)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _openWebLogin = openWebLogin;
        _sessionAppearanceChanged = sessionAppearanceChanged;
        _viewModel.StateChanged += OnStateChanged;
        Render();
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render();

            return false;
        });
    }

    private void Render()
    {
        var hasManualSession = _viewModel.HasManualSession;
        if (_editing)
        {
            account_stack.VisibleChildName = "manual";
            manual_heading.SetText(hasManualSession
                ? "Replace with manual session"
                : "Add manual session");
            return;
        }

        if (hasManualSession)
        {
            var displayName = _viewModel.DisplayName;
            signed_in_avatar.Text = displayName;
            signed_in_display_name.SetText(displayName);
            UpdateAvatar(_viewModel.AvatarUrl);
        }
        else
        {
            UpdateAvatar(null);
        }

        account_stack.VisibleChildName = hasManualSession ? "signed_in" : "signed_out";
        _sessionAppearanceChanged(hasManualSession, _viewModel.DisplayName, _avatarTexture);
    }

    private void UpdateAvatar(string? avatarUrl)
    {
        if (string.Equals(_avatarUrl, avatarUrl, StringComparison.Ordinal))
            return;

        _avatarUrl = avatarUrl;
        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        _avatarCancellation = null;
        signed_in_avatar.CustomImage = null!;
        _avatarTexture?.Dispose();
        _avatarTexture = null;

        if (string.IsNullOrWhiteSpace(avatarUrl))
            return;

        _avatarCancellation = new CancellationTokenSource();
        LoadAvatarAsync(avatarUrl, _avatarCancellation.Token).FireAndForget(Logger);
    }

    private async Task LoadAvatarAsync(string avatarUrl, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf;
        try
        {
            var thumbnail = await _thumbnails.GetThumbnailAsync(avatarUrl, cancellationToken).ConfigureAwait(false);
            if (thumbnail is null)
                return;

            pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(thumbnail.LocalPath, 128, 128, true),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load account avatar from {AvatarUrl}", avatarUrl);
            return;
        }

        var decodedPixbuf = pixbuf;
        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || !string.Equals(_avatarUrl, avatarUrl,
                        StringComparison.Ordinal))
                    return false;

                var pixbufForTexture = decodedPixbuf ??
                                       throw new InvalidOperationException("Avatar image decode returned no pixbuf.");
                var texture = Texture.NewForPixbuf(pixbufForTexture);
                pixbufForTexture.Dispose();
                decodedPixbuf = null;
                signed_in_avatar.CustomImage = texture;
                _avatarTexture = texture;
                _sessionAppearanceChanged(true, _viewModel.DisplayName, texture);
            }
            finally
            {
                decodedPixbuf?.Dispose();
            }

            return false;
        });
    }

    private void OpenManualEditor()
    {
        _editing = true;
        Render();
    }

    private void OnWebLoginClicked(object? sender, EventArgs args)
    {
        Logger.Information("AccountPopoverView web login button clicked");
        _openWebLogin();
    }

    private void OnOpenManualEditorClicked(object? sender, EventArgs args)
    {
        OpenManualEditor();
    }


    private void OnClearButtonClicked(object? sender, EventArgs args)
    {
        Logger.Information("AccountPopoverView clear session button clicked");
        _viewModel.ClearSession();
    }

    private void OnManualCancelButtonClicked(object? sender, EventArgs args)
    {
        _editing = false;
        Render();
    }

    private void OnManualSaveButtonClicked(object? sender, EventArgs args)
    {
        if (!_viewModel.SaveManualSession(GetText(manual_editor))) return;
        _editing = false;
        Render();
    }

    private static string GetText(TextView textView)
    {
        var buffer = textView.Buffer ??
                     throw new InvalidOperationException("Manual session editor text buffer was not initialized.");
        buffer.GetBounds(out var start, out var end);
        return buffer.GetText(start, end, true);
    }


    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _avatarCancellation?.Cancel();
        _avatarCancellation?.Dispose();
        signed_in_avatar.CustomImage = null!;
        _avatarTexture?.Dispose();
        _viewModel.StateChanged -= OnStateChanged;
        _viewModel.Dispose();
        base.Dispose();
    }
}