using Gdk;
using GdkPixbuf;
using Gio;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using Type = GObject.Type;

namespace SilverScreen.Views.Queue;

public partial class QueueItemRowView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<QueueItemRowView>();
    private const int ThumbnailWidth = 96;
    private const int ThumbnailHeight = 54;
    private readonly SimpleActionGroup _actions;
    private readonly Label _channel;
    private readonly WidgetPaintable _dragPaintable;
    private readonly DragSource _dragSource;
    private readonly Action<Guid, int> _dropRequested;
    private readonly DropTarget _dropTarget;
    private readonly Label _duration;
    private readonly Label _durationPill;
    private readonly Image _grip;
    private readonly SimpleAction _moveDownAction;
    private readonly Action<Guid, int> _moveRequested;
    private readonly SimpleAction _moveUpAction;
    private readonly Widget _placeholder;
    private readonly SimpleAction _playNowAction;
    private readonly Action<Guid, int>? _playRequested;
    private readonly Label _position;
    private readonly SimpleAction _removeAction;
    private readonly Action<Guid> _removeRequested;
    private readonly Stack _stateStack;
    private readonly Overlay _thumbnail;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private readonly Label _title;
    private readonly ProgressBar _watchedProgress;
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private bool _disposed;
    private int _index;
    private QueueItem? _item;
    public QueueItem? Item => _item;
    private CancellationTokenSource? _thumbnailCancellation;
    public QueueItemRowView(
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        Action<Guid, int> moveRequested,
        Action<Guid, int> dropRequested,
        Action<Guid> removeRequested,
        Action<Guid, int>? playRequested = null)
    {
        _thumbnails = thumbnails;
        _watchProgress = watchProgress;
        _moveRequested = moveRequested;
        _dropRequested = dropRequested;
        _removeRequested = removeRequested;
        _playRequested = playRequested;

        _grip = GetRequiredObject<Image>("grip");
        _stateStack = GetRequiredObject<Stack>("state_stack");
        _position = GetRequiredObject<Label>("position");
        _thumbnail = GetRequiredObject<Overlay>("thumbnail");
        _placeholder = GetRequiredObject<Widget>("placeholder");
        _durationPill = GetRequiredObject<Label>("duration_pill");
        _watchedProgress = GetRequiredObject<ProgressBar>("watched_progress");
        _title = GetRequiredObject<Label>("title");
        _channel = GetRequiredObject<Label>("channel");
        _duration = GetRequiredObject<Label>("duration");
        var menu = GetRequiredObject<MenuButton>("menu");

        _actions = SimpleActionGroup.New();
        _playNowAction = CreateAction("play-now", () =>
        {
            if (_item is { } item)
                _playRequested?.Invoke(item.Id, _index);
        });
        _moveUpAction = CreateAction("move-up", () => MoveBy(-1));
        _moveDownAction = CreateAction("move-down", () => MoveBy(1));
        _removeAction = CreateAction("remove", () =>
        {
            if (_item is { } item)
                _removeRequested(item.Id);
        });
        _actions.AddAction(_playNowAction);
        _actions.AddAction(_moveUpAction);
        _actions.AddAction(_moveDownAction);
        _actions.AddAction(_removeAction);
        menu.InsertActionGroup("queue", _actions);

        _dragSource = DragSource.New();
        _dragSource.Actions = DragAction.Move;
        _dragSource.OnPrepare += (_, _) =>
        {
            if (_item is not { } item)
                return null;

            using var value = new Value(item.Id.ToString());
            return ContentProvider.NewForValue(value);
        };
        _grip.AddController(_dragSource);
        _dragPaintable = WidgetPaintable.New(Widget);
        _dragSource.SetIcon(_dragPaintable, 0, 0);

        _dropTarget = DropTarget.New(Type.String, DragAction.Move);
        _dropTarget.OnDrop += (_, args) => HandleDrop(args.Value.GetString(), args.Y);
        Widget.AddController(_dropTarget);

        var detailsClick = GestureClick.New();
        detailsClick.OnReleased += (_, _) =>
        {
            if (_item is { } item)
                _playRequested?.Invoke(item.Id, _index);
        };
        var details = GetRequiredObject<Box>("details");
        details.AddController(detailsClick);

        var thumbnailClick = GestureClick.New();
        thumbnailClick.OnReleased += (_, _) =>
        {
            if (_item is { } item)
                _playRequested?.Invoke(item.Id, _index);
        };
        _thumbnail.AddController(thumbnailClick);
        _watchProgress.ProgressChanged += OnWatchProgressChanged;
    }

    private void OnRemoveButtonClicked(object? sender, EventArgs args)
    {
        if (_item is { } item)
            _removeRequested(item.Id);
    }

    public void Bind(QueueItem item, int index, int itemCount, int currentPlayingIndex = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unbind();
        _item = item;
        _index = index;
        _position.SetText((index + 1).ToString());
        _title.SetText(item.Video.Title);
        _channel.SetText(item.Video.ChannelName);
        var formattedDuration = FormatDuration(item.Video.Duration);
        _duration.SetText(formattedDuration);
        _durationPill.SetText(formattedDuration);
        SetWatchProgress(_watchProgress.GetFraction(item.Video.Id));
        _playNowAction.Enabled = _playRequested is not null && index != currentPlayingIndex;
        _moveUpAction.Enabled = index > 0;
        _moveDownAction.Enabled = index < itemCount - 1;
        _removeAction.Enabled = true;

        if (index == currentPlayingIndex)
        {
            _stateStack.VisibleChildName = "playing";
            Widget.AddCssClass("now-playing");
            Widget.RemoveCssClass("played");
        }
        else if (currentPlayingIndex >= 0 && index < currentPlayingIndex)
        {
            _stateStack.VisibleChildName = "index";
            Widget.RemoveCssClass("now-playing");
            Widget.AddCssClass("played");
        }
        else
        {
            _stateStack.VisibleChildName = "index";
            Widget.RemoveCssClass("now-playing");
            Widget.RemoveCssClass("played");
        }
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = new CancellationTokenSource();
        LoadThumbnailAsync(item.Video, generation, _thumbnailCancellation.Token).FireAndForget(Logger);
    }

    public void Unbind()
    {
        _item = null;
        _index = 0;
        _bindingGeneration++;
        _position.SetText(string.Empty);
        _title.SetText(string.Empty);
        _channel.SetText(string.Empty);
        _duration.SetText(string.Empty);
        _durationPill.SetText(string.Empty);
        _stateStack.VisibleChildName = "index";
        Widget.RemoveCssClass("now-playing");
        Widget.RemoveCssClass("played");
        _playNowAction.Enabled = false;
        _moveUpAction.Enabled = false;
        _moveDownAction.Enabled = false;
        _removeAction.Enabled = false;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
        SetWatchProgress(null);
    }

    private void OnWatchProgressChanged(object? sender, WatchProgress progress)
    {
        if (_item?.Video.Id != progress.VideoId)
            return;

        Functions.IdleAdd(0, () =>
        {
            if (!_disposed && _item?.Video.Id == progress.VideoId)
                SetWatchProgress(progress.Fraction);
            return false;
        });
    }

    private void SetWatchProgress(double? fraction)
    {
        var isVisible = fraction is > 0;
        _watchedProgress.SetVisible(isVisible);
        _watchedProgress.Fraction = isVisible ? fraction!.Value : 0;
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
                    () => Pixbuf.NewFromFileAtScale(result.LocalPath, ThumbnailWidth, ThumbnailHeight, true),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load thumbnail for queue item {VideoId}", video.Id);
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
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to render thumbnail texture for queue item {VideoId}", video.Id);
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
        // Release the paintable before detaching the widget.  This prevents a
        // replaced thumbnail texture from remaining alive until GTK later collects
        // the detached Picture.
        var picture = _boundPicture;
        var texture = _boundTexture;
        _boundPicture = null;
        _boundTexture = null;
        _thumbnail.Child = _placeholder;
        if (picture is not null)
        {
            picture.Paintable = null!;
            picture.Dispose();
        }

        texture?.Dispose();
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
        _watchProgress.ProgressChanged -= OnWatchProgressChanged;
        _grip.RemoveController(_dragSource);
        Widget.RemoveController(_dropTarget);
        _dragPaintable.Dispose();
        _dragSource.Dispose();
        _dropTarget.Dispose();
        _actions.Dispose();
        base.Dispose();
    }
}