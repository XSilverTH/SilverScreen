using Adw;
using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;
using SilverScreen.Player;
using SilverScreen.Shell.Notifications;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;
using Menu = Gio.Menu;
using Action = System.Action;
using AlertDialog = Adw.AlertDialog;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;
using Window = Gtk.Window;

namespace SilverScreen.Downloads;

public partial class LibraryItemCardView : ViewBase<Bin>
{
    private const int CardWidth = 336, ThumbnailHeight = 189;
    private static readonly ILogger Logger = Log.ForContext<LibraryItemCardView>();
    private static readonly SemaphoreSlim DecodeSemaphore = new(4, 4);

    private readonly SimpleActionGroup _actionGroup = SimpleActionGroup.New();
    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly IThumbnailService _thumbnails;
    private readonly NativeMemoryPressure _thumbnailPressure = new();

    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private LibraryEntry? _entry;
    private PopoverMenu? _popoverMenu;
    private CancellationTokenSource? _thumbCts;

    public LibraryItemCardView(
        ILibraryService library,
        IThumbnailService thumbnails,
        INavigationService navigation,
        IPlaybackService playback,
        IQueueService queue,
        INotificationService notifications)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        SetupMenu();

        var click = GestureClick.New();
        click.Button = 0;
        Lifetime.Attach(card, click, c => c.OnReleased += OnCardReleased, c => c.OnReleased -= OnCardReleased);

        var rightClick = GestureClick.New();
        rightClick.Button = 3;
        Lifetime.Attach(card, rightClick, c => c.OnPressed += OnCardRightClicked, c => c.OnPressed -= OnCardRightClicked);

        var channelClick = GestureClick.New();
        channelClick.Button = 1;
        Lifetime.Attach(channel_label, channelClick, c => c.OnReleased += OnChannelClicked,
            c => c.OnReleased -= OnChannelClicked);

        var key = EventControllerKey.New();
        Lifetime.Attach(card, key, c => c.OnKeyPressed += OnKeyPressed, c => c.OnKeyPressed -= OnKeyPressed);
    }

    public void Bind(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;

        title_label.SetText(entry.Video.Title);
        channel_label.SetText(entry.Video.ChannelName);
        duration_label.SetText(VideoCardFormatting.FormatDuration(entry.Video.Duration));
        size_label.SetText(DownloadActionHelper.FormatBytes(entry.FileSizeBytes));

        var isAvailable = entry.IsFileAvailable(_library.LibraryDirectory);
        format_pill.RemoveCssClass("library-missing-badge");
        if (!isAvailable)
        {
            format_pill.AddCssClass("library-missing-badge");
            format_pill.SetText("Missing file");
        }
        else
        {
            var badge = string.IsNullOrWhiteSpace(entry.Container)
                ? entry.QualityLabel
                : $"{entry.QualityLabel} · {entry.Container}";
            format_pill.SetText(badge);
        }

        card.TooltipText = $"{entry.Video.Title} — {entry.Video.ChannelName}";
        LoadThumbnail(entry);
    }

    public void Unbind()
    {
        _entry = null;
        _bindingGeneration++;
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = null;
        _popoverMenu?.Popdown();
        ClearThumbnail();
    }

    private void SetupMenu()
    {
        AddMenuAction("play", () =>
        {
            if (_entry is { } e) Play(e, false);
        });
        AddMenuAction("play-alternate", () =>
        {
            if (_entry is { } e) Play(e, true);
        });
        AddMenuAction("add-to-queue", () =>
        {
            if (_entry is { } e) AddToQueue(e);
        });
        AddMenuAction("open-folder", () =>
        {
            if (_entry is { } e) OpenContainingFolder(e);
        });
        AddMenuAction("open-channel", () =>
        {
            if (_entry is { } e) OpenChannel(e);
        });
        AddMenuAction("remove-index", () =>
        {
            if (_entry is { } e)
                _library.RemoveAsync(e.Video.Id, deleteFileFromDisk: false).FireAndForget(Logger);
        });
        AddMenuAction("delete-disk", () =>
        {
            if (_entry is { } e) ConfirmDeleteFromDisk(e);
        });

        var menu = Menu.New();
        menu.Append("Play", "libcard.play");
        menu.Append("Open in other player", "libcard.play-alternate");
        menu.Append("Add to queue", "libcard.add-to-queue");
        menu.Append("Open containing folder", "libcard.open-folder");
        menu.Append("Open channel", "libcard.open-channel");
        menu.Append("Remove from Library", "libcard.remove-index");
        menu.Append("Delete file from disk", "libcard.delete-disk");

        menu_button.MenuModel = menu;
        menu_button.InsertActionGroup("libcard", _actionGroup);
        card.InsertActionGroup("libcard", _actionGroup);

        _popoverMenu = PopoverMenu.NewFromModel(menu);
        _popoverMenu.HasArrow = false;
        _popoverMenu.SetParent(card);
        _popoverMenu.InsertActionGroup("libcard", _actionGroup);
    }

    private void AddMenuAction(string name, Action callback)
    {
        var action = SimpleAction.New(name, null);
        action.OnActivate += (_, _) => callback();
        _actionGroup.AddAction(action);
    }

    private void LoadThumbnail(LibraryEntry entry)
    {
        _thumbCts?.Cancel();
        _thumbCts?.Dispose();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;
        var generation = ++_bindingGeneration;
        ClearThumbnail();

        LoadThumbnailAsync(entry, generation, ct).FireAndForget(Logger);
    }

    private async Task LoadThumbnailAsync(LibraryEntry entry, int generation, CancellationToken ct)
    {
        var acquired = false;
        Pixbuf? pixbuf;
        try
        {
            var localThumb = entry.ResolveThumbnailPath(_library.LibraryDirectory);
            if (string.IsNullOrWhiteSpace(localThumb) || !File.Exists(localThumb))
            {
                var fetched = await _thumbnails.GetThumbnailAsync(entry.Video, ct).ConfigureAwait(false);
                localThumb = fetched?.LocalPath;
            }

            if (string.IsNullOrWhiteSpace(localThumb) || !File.Exists(localThumb) ||
                IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation)
                return;

            await DecodeSemaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation)
                return;

            pixbuf = await Task
                .Run(() => Pixbuf.NewFromFileAtScale(localThumb!, CardWidth, ThumbnailHeight, true), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }
        finally
        {
            if (acquired)
                DecodeSemaphore.Release();
        }

        if (pixbuf is null) return;
        GtkUiDispatch.Run(() => !IsDisposed && !ct.IsCancellationRequested && _bindingGeneration == generation, () =>
        {
            try
            {
                var texture = Texture.NewForPixbuf(pixbuf);
                if (_boundPicture is null)
                {
                    _boundPicture = Picture.NewForPaintable(texture);
                    _boundPicture.ContentFit = ContentFit.Cover;
                    _boundPicture.HeightRequest = ThumbnailHeight;
                    _boundPicture.Hexpand = _boundPicture.Vexpand = true;
                }
                else
                {
                    _boundPicture.Paintable = texture;
                }

                thumb_overlay.Child = _boundPicture;
                _boundTexture?.Dispose();
                _boundTexture = texture;
                _thumbnailPressure.Track((long)CardWidth * ThumbnailHeight * 4);
            }
            finally
            {
                pixbuf.Dispose();
            }
        });
    }

    private void ClearThumbnail()
    {
        var texture = _boundTexture;
        _boundTexture = null;
        _thumbnailPressure.Release();
        thumb_overlay.Child = placeholder;
        if (_boundPicture is not null)
            _boundPicture.Paintable = null!;
        texture?.Dispose();
    }

    private void Play(LibraryEntry entry, bool alternate)
    {
        if (!entry.IsFileAvailable(_library.LibraryDirectory))
        {
            _notifications.ShowToast("Downloaded file is missing from disk.");
            return;
        }

        var playable = entry.ToPlayableVideoSummary(_library.LibraryDirectory);
        PlayInternalAsync(playable, alternate).FireAndForget(Logger);
    }

    private async Task PlayInternalAsync(VideoSummary video, bool alternate)
    {
        try
        {
            var result = alternate
                ? await _playback.PlayAlternateAsync(new PlaybackRequest([video])).ConfigureAwait(false)
                : await _playback.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result))
                _notifications.ShowToast(result);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to play Library item");
            _notifications.ShowToast($"Could not play “{video.Title}”.");
        }
    }

    private void AddToQueue(LibraryEntry entry)
    {
        var video = entry.IsFileAvailable(_library.LibraryDirectory)
            ? entry.ToPlayableVideoSummary(_library.LibraryDirectory)
            : entry.Video;
        _queue.Add(video);
        _notifications.ShowToast($"Added “{entry.Video.Title}” to queue");
    }

    private void OpenContainingFolder(LibraryEntry entry)
    {
        var filePath = entry.ResolveFilePath(_library.LibraryDirectory);
        var folder = Path.GetDirectoryName(filePath) ?? _library.LibraryDirectory;
        if (!Directory.Exists(folder) || Widget.GetRoot() is not Window window)
            return;

        try
        {
            var uri = new Uri(folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar)
                .AbsoluteUri;
            UriLauncher.New(uri).LaunchAsync(window).FireAndForget(Logger);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to open containing folder {Folder}", folder);
        }
    }

    private void OpenChannel(LibraryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Video.ChannelUrl) &&
            !entry.Video.ChannelUrl.Contains("UC0000000000000000000000", StringComparison.OrdinalIgnoreCase))
            _navigation.NavigateToChannel(entry.Video.ChannelUrl, entry.Video.ChannelName);
    }

    private void ConfirmDeleteFromDisk(LibraryEntry entry)
    {
        var dialog = AlertDialog.New(
            "Delete downloaded file?",
            $"“{entry.Video.Title}” will be removed from your Library and deleted from disk.");
        dialog.AddResponse("cancel", "Cancel");
        dialog.AddResponse("delete", "Delete");
        dialog.SetResponseAppearance("delete", ResponseAppearance.Destructive);
        dialog.DefaultResponse = "cancel";
        dialog.CloseResponse = "cancel";

        dialog.OnResponse += (_, args) =>
        {
            if (string.Equals(args.Response, "delete", StringComparison.Ordinal))
                _library.RemoveAsync(entry.Video.Id, deleteFileFromDisk: true).FireAndForget(Logger);
        };

        dialog.Present(Widget);
    }

    private void OnCardReleased(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_entry is not { } e) return;
        if (sender.GetCurrentButton() == 1) Play(e, false);
        else if (sender.GetCurrentButton() == 2) AddToQueue(e);
    }

    private void OnCardRightClicked(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (_entry is null || _popoverMenu is null) return;
        sender.SetState(EventSequenceState.Claimed);
        _popoverMenu.SetPointingTo(new Rectangle
        {
            X = (int)args.X,
            Y = (int)args.Y,
            Width = 1,
            Height = 1
        });
        _popoverMenu.Popup();
    }

    private void OnChannelClicked(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_entry is not { } e) return;
        sender.SetState(EventSequenceState.Claimed);
        OpenChannel(e);
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (IsDisposed || _entry is null) return false;
        switch (args.Keyval)
        {
            case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space:
                Play(_entry, false);
                return true;
            case Constants.KEY_Menu or Constants.KEY_F10 when (args.State & ModifierType.ShiftMask) != 0:
                if (_popoverMenu is not null)
                {
                    _popoverMenu.SetPointingTo(new Rectangle
                    {
                        X = Math.Max(0, card.GetWidth() / 2),
                        Y = Math.Max(0, card.GetHeight() / 2),
                        Width = 1,
                        Height = 1
                    });
                    _popoverMenu.Popup();
                }

                return true;
            default:
                return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
            if (_popoverMenu is not null)
            {
                _popoverMenu.Popdown();
                _popoverMenu.Unparent();
                _popoverMenu.Dispose();
                _popoverMenu = null;
            }

            _boundPicture?.Dispose();
            _boundPicture = null;
            _thumbnailPressure.Dispose();
            _actionGroup.Dispose();
        }

        base.Dispose(disposing);
    }
}
