using Adw;
using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Account.Profile;

public partial class AccountPopoverView : ViewBase<Bin>
{
    private static readonly ILogger Logger = Log.ForContext<AccountPopoverView>();
    private readonly Action _openWebLogin;
    private readonly Action<bool, string, Texture?> _sessionAppearanceChanged;
    private readonly IThumbnailService _thumbnails;
    private readonly AccountViewModel _viewModel;
    private CancellationTokenSource? _avatarCancellation;
    private Texture? _avatarTexture;
    private string? _avatarUrl;
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
        Lifetime.Track(() => _viewModel.StateChanged += OnStateChanged, () => _viewModel.StateChanged -= OnStateChanged);
        Lifetime.Track(() => Widget.OnUnmap += OnWidgetUnmap, () => Widget.OnUnmap -= OnWidgetUnmap);
        Render();
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (IsDisposed) return;
        try
        {
            Lifetime.Idle(() =>
            {
                if (!IsDisposed)
                    Render();

                return false;
            });
        }
        catch (ObjectDisposedException)
        {
        }
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
        if (IsDisposed || cancellationToken.IsCancellationRequested)
        {
            decodedPixbuf?.Dispose();
            return;
        }

        try
        {
            Lifetime.Idle(() =>
            {
                try
                {
                    if (IsDisposed || cancellationToken.IsCancellationRequested || !string.Equals(_avatarUrl, avatarUrl,
                            StringComparison.Ordinal))
                        return false;

                    var pixbufForTexture = decodedPixbuf ??
                                           throw new InvalidOperationException("Avatar image decode returned no pixbuf.");
                    var texture = Texture.NewForPixbuf(pixbufForTexture);
                    pixbufForTexture.Dispose();
                    decodedPixbuf = null;
                    signed_in_avatar.CustomImage = texture;
                    _avatarTexture?.Dispose();
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
        catch (ObjectDisposedException)
        {
            decodedPixbuf?.Dispose();
        }
    }

    private void OpenManualEditor()
    {
        ClearBuffer(manual_editor);
        _editing = true;
        manual_error_label.SetVisible(false);
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
        ClearBuffer(manual_editor);
        _viewModel.ClearSession();
    }

    private void OnWidgetUnmap(Widget sender, EventArgs args)
    {
        ClearBuffer(manual_editor);
        if (!_editing) return;
        _editing = false;
        manual_error_label.SetVisible(false);
        Render();
    }

    private void OnManualCancelButtonClicked(object? sender, EventArgs args)
    {
        _editing = false;
        manual_error_label.SetVisible(false);
        ClearBuffer(manual_editor);
        Render();
    }

    private void OnManualSaveButtonClicked(object? sender, EventArgs args)
    {
        if (!_viewModel.SaveManualSession(GetText(manual_editor)))
        {
            manual_error_label.SetText(_viewModel.ManualSessionError ?? "Could not save the session. Try again.");
            manual_error_label.SetVisible(true);
            return;
        }

        ClearBuffer(manual_editor);
        manual_error_label.SetVisible(false);
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

    private static void ClearBuffer(TextView textView)
    {
        var buffer = textView.Buffer;

        buffer?.SetText(string.Empty, 0);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearBuffer(manual_editor);
            _avatarCancellation?.Cancel();
            _avatarCancellation?.Dispose();
            _avatarCancellation = null;
            signed_in_avatar.CustomImage = null!;
            _avatarTexture?.Dispose();
            _avatarTexture = null;
            _viewModel.Dispose();
        }

        base.Dispose(disposing);
    }
}