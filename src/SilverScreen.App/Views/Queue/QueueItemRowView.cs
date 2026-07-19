using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Pango;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Views.Queue;

public partial class QueueItemRowView : ViewBase<Box>
{
    private const int ThumbnailWidth = 96;
    private const int ThumbnailHeight = 54;
    private readonly SimpleAction _moveDownAction;
    private readonly Action<Guid, int> _moveRequested;
    private readonly SimpleAction _moveUpAction;
    private readonly Action<Guid, int> _dropRequested;
    private readonly Action<Guid> _removeRequested;
    private readonly SimpleAction _removeAction;
    private readonly IThumbnailService _thumbnails;
    private readonly Label _channel;
    private readonly Label _duration;
    private readonly Image _grip;
    private readonly Label _position;
    private readonly Label _title;
    private readonly Overlay _thumbnail;
    private readonly Widget _placeholder;
    private readonly MenuButton _menu;
    private readonly SimpleActionGroup _actions;
    private readonly Menu _menuModel;
    private readonly DragSource _dragSource;
    private readonly DropTarget _dropTarget;
    private readonly WidgetPaintable _dragPaintable;
    private CancellationTokenSource? _thumbnailCancellation;
    private Texture? _boundTexture;
    private Picture? _boundPicture;
    private QueueItem? _item;
    private int _index;
    private int _itemCount;
    private int _bindingGeneration;
    private bool _disposed;

    public QueueItemRowView(
        IThumbnailService thumbnails,
        Action<Guid, int> moveRequested,
        Action<Guid, int> dropRequested,
        Action<Guid> removeRequested)
    {
        _thumbnails = thumbnails;
        _moveRequested = moveRequested;
        _dropRequested = dropRequested;
        _removeRequested = removeRequested;
        Widget.CssClasses = ["queue-row"];
        Widget.Spacing = 8;
        Widget.MarginTop = 3;
        Widget.MarginBottom = 3;
        Widget.MarginStart = 8;
        Widget.MarginEnd = 8;
        Widget.HeightRequest = 78;
        Widget.Valign = Align.Start;

        _grip = Image.NewFromIconName("list-drag-handle-symbolic");
        _grip.TooltipText = "Drag to reorder";
        _grip.MarginStart = 2;
        _grip.MarginEnd = 2;
        _grip.Valign = Align.Center;
        Widget.Append(_grip);

        _position = Label.New(string.Empty);
        _position.CssClasses = ["caption", "dim-label"];
        _position.Valign = Align.Start;
        Widget.Append(_position);

        _thumbnail = Overlay.New();
        _thumbnail.WidthRequest = ThumbnailWidth;
        _thumbnail.HeightRequest = ThumbnailHeight;
        _thumbnail.CssClasses = ["queue-thumbnail"];
        _thumbnail.Overflow = Overflow.Hidden;
        _placeholder = Image.NewFromIconName("media-playback-start-symbolic");
        _placeholder.Halign = Align.Center;
        _placeholder.Valign = Align.Center;
        _thumbnail.Child = _placeholder;
        Widget.Append(_thumbnail);

        var details = Box.New(Orientation.Vertical, 3);
        details.Hexpand = true;
        details.Valign = Align.Center;
        _title = Label.New(string.Empty);
        _title.Xalign = 0;
        _title.Lines = 2;
        _title.Ellipsize = EllipsizeMode.End;
        _title.Wrap = true;
        _title.Hexpand = true;
        details.Append(_title);
        var metadata = Box.New(Orientation.Horizontal, 6);
        _channel = Label.New(string.Empty);
        _channel.Xalign = 0;
        _channel.Ellipsize = EllipsizeMode.End;
        _channel.Hexpand = true;
        _channel.CssClasses = ["caption", "dim-label"];
        metadata.Append(_channel);
        _duration = Label.New(string.Empty);
        _duration.Xalign = 1;
        _duration.CssClasses = ["caption", "dim-label"];
        metadata.Append(_duration);
        details.Append(metadata);
        Widget.Append(details);

        var remove = Button.NewFromIconName("user-trash-symbolic");
        remove.TooltipText = "Remove from queue";
        remove.CssClasses = ["flat", "circular"];
        remove.Valign = Align.Center;
        remove.OnClicked += (_, _) =>
        {
            if (_item is { } item)
                _removeRequested(item.Id);
        };
        Widget.Append(remove);

        _actions = SimpleActionGroup.New();
        _moveUpAction = CreateAction("move-up", () => MoveBy(-1));
        _moveDownAction = CreateAction("move-down", () => MoveBy(1));
        _removeAction = CreateAction("remove", () =>
        {
            if (_item is { } item)
                _removeRequested(item.Id);
        });
        _actions.AddAction(_moveUpAction);
        _actions.AddAction(_moveDownAction);
        _actions.AddAction(_removeAction);
        _menuModel = Menu.New();
        _menuModel.Append("Move up", "queue.move-up");
        _menuModel.Append("Move down", "queue.move-down");
        _menuModel.Append("Remove", "queue.remove");
        _menu = MenuButton.New();
        _menu.IconName = "view-more-symbolic";
        _menu.TooltipText = "Queue item actions";
        _menu.CssClasses = ["flat", "circular"];
        _menu.Valign = Align.Center;
        _menu.InsertActionGroup("queue", _actions);
        _menu.MenuModel = _menuModel;
        Widget.Append(_menu);

        _dragSource = DragSource.New();
        _dragSource.Actions = DragAction.Move;
        _dragSource.OnPrepare += (_, _) =>
        {
            if (_item is not { } item)
                return null;

            using var value = new GObject.Value(item.Id.ToString());
            return ContentProvider.NewForValue(value);
        };
        _grip.AddController(_dragSource);
        _dragPaintable = WidgetPaintable.New(Widget);
        _dragSource.SetIcon(_dragPaintable, 0, 0);

        _dropTarget = DropTarget.New(GObject.Type.String, DragAction.Move);
        _dropTarget.OnDrop += (_, args) => HandleDrop(args.Value.GetString(), args.Y);
        Widget.AddController(_dropTarget);
    }

    public void Bind(QueueItem item, int index, int itemCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unbind();
        _item = item;
        _index = index;
        _itemCount = itemCount;
        _position.SetText((index + 1).ToString());
        _title.SetText(item.Video.Title);
        _channel.SetText(item.Video.ChannelName);
        _duration.SetText(FormatDuration(item.Video.Duration));
        _moveUpAction.Enabled = index > 0;
        _moveDownAction.Enabled = index < itemCount - 1;
        _removeAction.Enabled = true;
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = new CancellationTokenSource();
        _ = LoadThumbnailAsync(item.Video, generation, _thumbnailCancellation.Token);
    }

    public void Unbind()
    {
        _item = null;
        _index = 0;
        _itemCount = 0;
        _bindingGeneration++;
        _position.SetText(string.Empty);
        _title.SetText(string.Empty);
        _channel.SetText(string.Empty);
        _duration.SetText(string.Empty);
        _moveUpAction.Enabled = false;
        _moveDownAction.Enabled = false;
        _removeAction.Enabled = false;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
    }

    private bool HandleDrop(string? value, double y)
    {
        if (_item is null || !Guid.TryParse(value, out var itemId))
            return false;

        Widget.RemoveCssClass("queue-drop-before");
        Widget.RemoveCssClass("queue-drop-after");
        _dropRequested(itemId, y < Widget.HeightRequest / 2.0 ? _index : _index + 1);
        return true;
    }

    private void MoveBy(int delta)
    {
        if (_item is { } item)
            _moveRequested(item.Id, _index + delta);
    }

    private async Task LoadThumbnailAsync(VideoSummary video, int generation, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(video, cancellationToken).ConfigureAwait(false);
            if (result is null)
                return;

            pixbuf = await Task.Run(
                () => Pixbuf.NewFromFileAtScale(result.LocalPath, ThumbnailWidth, ThumbnailHeight, true), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Thumbnail decode returned no pixbuf.");
        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || _bindingGeneration != generation ||
                    _thumbnail.GetRoot() is null)
                    return false;

                Texture? texture = null;
                Picture? picture = null;
                try
                {
                    texture = Texture.NewForPixbuf(decodedPixbuf);
                    decodedPixbuf.Dispose();
                    decodedPixbuf = null;
                    picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = $"{video.Title} thumbnail";
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = ThumbnailWidth;
                    picture.HeightRequest = ThumbnailHeight;
                    picture.Hexpand = true;
                    picture.Vexpand = true;
                    ClearThumbnail();
                    _thumbnail.Child = picture;
                    _boundTexture = texture;
                    _boundPicture = picture;
                    texture = null;
                    picture = null;
                }
                finally
                {
                    picture?.Dispose();
                    texture?.Dispose();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                decodedPixbuf?.Dispose();
            }

            return false;
        });
    }

    private void ClearThumbnail()
    {
        _thumbnail.Child = _placeholder;
        _boundPicture?.Dispose();
        _boundPicture = null;
        _boundTexture?.Dispose();
        _boundTexture = null;
    }

    private static SimpleAction CreateAction(string name, Action callback)
    {
        var action = SimpleAction.New(name, null);
        action.OnActivate += (_, _) => callback();
        return action;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m"
                : $"{duration.Seconds}s";
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unbind();
        _grip.RemoveController(_dragSource);
        Widget.RemoveController(_dropTarget);
        _dragPaintable.Dispose();
        _dragSource.Dispose();
        _dropTarget.Dispose();
        _actions.Dispose();
        _menuModel.Dispose();
        base.Dispose();
    }
}
