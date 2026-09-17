using Gdk;
using GdkPixbuf;
using Gio;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Queue;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using Type = GObject.Type;

namespace SilverScreen.Queue;

internal sealed class QueueRowBinding
{
    public QueueItem? Item { get; private set; }
    public int Index { get; private set; } = -1;

    public void Bind(QueueItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public void Unbind()
    {
        Item = null;
        Index = -1;
    }
    
    public bool TryGetPlayTarget(out Guid itemId, out int index)
    {
        if (Item is { } item && Index >= 0)
        {
            itemId = item.Id;
            index = Index;
            return true;
        }

        itemId = default;
        index = -1;
        return false;
    }

    public bool TryGetMoveTarget(int delta, out Guid itemId, out int destinationIndex)
    {
        if (Item is { } item && Index >= 0)
        {
            itemId = item.Id;
            destinationIndex = Index + delta;
            return true;
        }

        itemId = default;
        destinationIndex = -1;
        return false;
    }

    public int ResolveDropIndex(bool dropBefore) => Index < 0 ? 0 : dropBefore ? Index : Index + 1;
}
public partial class QueueItemRowView : ViewBase<Box>
{
    private const int ThumbnailWidth = 96;
    private const int ThumbnailHeight = 54;
    private static readonly ILogger Logger = Log.ForContext<QueueItemRowView>();
    private readonly SimpleActionGroup _actions;
    private readonly WidgetPaintable _dragPaintable;
    private readonly DragSource _dragSource;
    private readonly Action<Guid, int> _dropRequested;
    private readonly DropTarget _dropTarget;
    private readonly SimpleAction _moveDownAction;
    private readonly Action<Guid, int> _moveRequested;
    private readonly SimpleAction _moveUpAction;
    private readonly SimpleAction _playNowAction;
    private readonly Action<Guid, int>? _playRequested;
    private readonly Action<Guid> _removeRequested;
    private readonly IThumbnailService _thumbnails;
    private int _bindingGeneration;
    private readonly QueueRowBinding _binding = new();
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private CancellationTokenSource? _thumbnailCancellation;

    public QueueItemRowView(
        IThumbnailService thumbnails,
        Action<Guid, int> moveRequested,
        Action<Guid, int> dropRequested,
        Action<Guid> removeRequested,
        Action<Guid, int>? playRequested = null)
    {
        _thumbnails = thumbnails;
        _moveRequested = moveRequested;
        _dropRequested = dropRequested;
        _removeRequested = removeRequested;
        _playRequested = playRequested;


        _actions = Lifetime.Own(SimpleActionGroup.New());
        _playNowAction = CreateAction("play-now", () =>
        {
            if (_binding.TryGetPlayTarget(out var itemId, out var index))
                _playRequested?.Invoke(itemId, index);
        });
        _moveUpAction = CreateAction("move-up", () => MoveBy(-1));
        _moveDownAction = CreateAction("move-down", () => MoveBy(1));
        _actions.AddAction(_playNowAction);
        _actions.AddAction(_moveUpAction);
        _actions.AddAction(_moveDownAction);
        menu.InsertActionGroup("queue", _actions);

        _dragPaintable = Lifetime.Own(WidgetPaintable.New(Widget));
        _dragSource = DragSource.New();
        _dragSource.Actions = DragAction.Move;
        _dragSource.SetIcon(_dragPaintable, 0, 0);
        Lifetime.Attach(grip, _dragSource,
            c => c.OnPrepare += OnDragPrepare,
            c => c.OnPrepare -= OnDragPrepare);

        _dropTarget = DropTarget.New(Type.String, DragAction.Move);
        Lifetime.Attach(Widget, _dropTarget,
            c =>
            {
                c.OnMotion += OnDropMotion;
                c.OnLeave += OnDropLeave;
                c.OnDrop += OnDrop;
            },
            c =>
            {
                c.OnMotion -= OnDropMotion;
                c.OnLeave -= OnDropLeave;
                c.OnDrop -= OnDrop;
            });

        var detailsClick = GestureClick.New();
        Lifetime.Attach(details, detailsClick,
            c => c.OnReleased += OnDetailsOrThumbnailClicked,
            c => c.OnReleased -= OnDetailsOrThumbnailClicked);

        var thumbnailClick = GestureClick.New();
        Lifetime.Attach(thumbnail, thumbnailClick,
            c => c.OnReleased += OnDetailsOrThumbnailClicked,
            c => c.OnReleased -= OnDetailsOrThumbnailClicked);
    }

    private ContentProvider? OnDragPrepare(DragSource sender, DragSource.PrepareSignalArgs args)
    {
        if (_binding.Item is not { } item)
            return null;

        using var value = new Value(item.Id.ToString());
        return ContentProvider.NewForValue(value);
    }

    private bool OnDrop(DropTarget sender, DropTarget.DropSignalArgs args)
    {
        return HandleDrop(args.Value.GetString(), args.Y);
    }

    private void OnDetailsOrThumbnailClicked(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_binding.TryGetPlayTarget(out var itemId, out var index))
            _playRequested?.Invoke(itemId, index);
    }

    private void OnRemoveButtonClicked(object? sender, EventArgs args)
    {
        if (Item is { } item)
            _removeRequested(item.Id);
    }

    public QueueItem? Item => _binding.Item;


    public void Bind(QueueItem item, int index, int itemCount, int currentPlayingIndex = -1)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Unbind();
        _binding.Bind(item, index);
        position.SetText((index + 1).ToString());
        title.SetText(item.Video.Title);
        channel.SetText(item.Video.ChannelName);
        duration_pill.SetText(FormatDuration(item.Video.Duration));
        SetWatchProgress(item.Video.PlaybackProgress?.WatchedFraction);
        _playNowAction.Enabled = _playRequested is not null && index != currentPlayingIndex;
        _moveUpAction.Enabled = index > 0;
        _moveDownAction.Enabled = index < itemCount - 1;

        var isPlaying = index == currentPlayingIndex;
        position.SetVisible(!isPlaying);
        playing_icon.SetVisible(isPlaying);
        if (isPlaying)
        {
            Widget.AddCssClass("now-playing");
            Widget.RemoveCssClass("played");
        }
        else if (currentPlayingIndex >= 0 && index < currentPlayingIndex)
        {
            Widget.RemoveCssClass("now-playing");
            Widget.AddCssClass("played");
        }
        else
        {
            Widget.RemoveCssClass("now-playing");
            Widget.RemoveCssClass("played");
        }

        var generation = ++_bindingGeneration;
        _thumbnailCancellation = new CancellationTokenSource();
        LoadThumbnailAsync(item.Video, generation, _thumbnailCancellation.Token).FireAndForget(Logger);
    }

    public void Unbind()
    {
        _binding.Unbind();
        _bindingGeneration++;
        position.SetText(string.Empty);
        title.SetText(string.Empty);
        channel.SetText(string.Empty);
        duration_pill.SetText(string.Empty);
        position.SetVisible(true);
        playing_icon.SetVisible(false);
        Widget.RemoveCssClass("now-playing");
        Widget.RemoveCssClass("played");
        Widget.RemoveCssClass("queue-drop-before");
        Widget.RemoveCssClass("queue-drop-after");
        _playNowAction.Enabled = false;
        _moveUpAction.Enabled = false;
        _moveDownAction.Enabled = false;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
        SetWatchProgress(null);
    }


    private void SetWatchProgress(double? fraction)
    {
        var isVisible = fraction is > 0;
        watched_progress.SetVisible(isVisible);
        watched_progress.Fraction = isVisible ? fraction!.Value : 0;
    }

    private bool HandleDrop(string? value, double y)
    {
        Widget.RemoveCssClass("queue-drop-before");
        Widget.RemoveCssClass("queue-drop-after");

        if (!Guid.TryParse(value, out var itemId) || _binding.Item is null)
            return false;

        _dropRequested(itemId, _binding.ResolveDropIndex(y >= Widget.GetAllocatedHeight() / 2.0));
        return true;
    }

    private DragAction OnDropMotion(DropTarget sender, DropTarget.MotionSignalArgs args)
    {
        if (_binding.Item is null)
            return 0;

        if (args.Y < Widget.GetAllocatedHeight() / 2.0)
        {
            Widget.AddCssClass("queue-drop-before");
            Widget.RemoveCssClass("queue-drop-after");
        }
        else
        {
            Widget.RemoveCssClass("queue-drop-before");
            Widget.AddCssClass("queue-drop-after");
        }

        return DragAction.Move;
    }

    private void OnDropLeave(DropTarget sender, EventArgs args)
    {
        Widget.RemoveCssClass("queue-drop-before");
        Widget.RemoveCssClass("queue-drop-after");
    }

    private void MoveBy(int delta)
    {
        if (_binding.TryGetMoveTarget(delta, out var itemId, out var destinationIndex))
            _moveRequested(itemId, destinationIndex);
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
        if (IsDisposed || cancellationToken.IsCancellationRequested)
        {
            decodedPixbuf.Dispose();
            return;
        }

        try
        {
            Lifetime.Idle(() =>
            {
                try
                {
                    if (IsDisposed || cancellationToken.IsCancellationRequested || _bindingGeneration != generation ||
                        thumbnail.GetRoot() is null)
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
                        thumbnail.Child = picture;
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
        catch (ObjectDisposedException)
        {
            decodedPixbuf?.Dispose();
        }
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
        thumbnail.Child = placeholder;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Unbind();
        }

        base.Dispose(disposing);
    }
}