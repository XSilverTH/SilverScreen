using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Task = System.Threading.Tasks.Task;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Components;

public sealed class VideoCardActions
{
    public required Func<VideoSummary, Task> PlayAsync { get; init; }
    public required Func<VideoSummary, Task> OpenInAlternatePlayerAsync { get; init; }
    public required Action<VideoSummary> AddToQueue { get; init; }
    public Func<VideoSummary, Task>? OpenChannelAsync { get; init; }
}

public partial class VideoCardView : ViewBase<Box>
{
    private const int CardWidth = 336;
    private const int ThumbnailHeight = 189;
    private static readonly ILogger Logger = Log.ForContext<VideoCardView>();
    private readonly VideoCardActions _actions;
    private readonly GestureClick _channelClick;
    private readonly GestureClick _click;
    private readonly PopoverMenu _contextMenu;
    private readonly SimpleAction[] _menuActionItems;
    private readonly SimpleActionGroup _menuActions;
    private readonly GestureClick _rightClick;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private bool _disposed;
    private string _thumbnailAlternativeText = string.Empty;
    private CancellationTokenSource? _thumbnailCancellation;
    private VideoSummary? _video;

    public VideoCardView(IThumbnailService thumbnails, IWatchProgressService watchProgress, VideoCardActions actions)
    {
        _thumbnails = thumbnails;
        _watchProgress = watchProgress;
        _actions = actions;


        _menuActions = SimpleActionGroup.New();
        _menuActionItems =
        [
            CreateMenuAction("play"),
            CreateMenuAction("play-alternate"),
            CreateMenuAction("add-to-queue"),
            CreateMenuAction("open-channel"),
            CreateMenuAction("copy-link")
        ];
        foreach (var action in _menuActionItems)
        {
            action.OnActivate += OnMenuActionActivated;
            _menuActions.AddAction(action);
        }

        menu.InsertActionGroup("video", _menuActions);
        card.InsertActionGroup("video", _menuActions);

        _contextMenu = PopoverMenu.NewFromModel(menu.MenuModel!);
        _contextMenu.SetParent(card);
        _contextMenu.HasArrow = false;
        _contextMenu.InsertActionGroup("video", _menuActions);

        _click = GestureClick.New();
        _click.Button = 0;
        _click.OnReleased += OnCardReleased;
        card.AddController(_click);

        _rightClick = GestureClick.New();
        _rightClick.Button = 3;
        _rightClick.OnPressed += OnCardRightClicked;
        card.AddController(_rightClick);
        _channelClick = GestureClick.New();
        _channelClick.Button = 1;
        _channelClick.OnReleased += OnChannelReleased;
        channel.AddController(_channelClick);
        _watchProgress.ProgressChanged += OnWatchProgressChanged;
    }

    public void Bind(VideoSummary video, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unbind();

        _video = video;
        title.SetText(video.Title);
        title.TooltipText = video.Title;
        channel.SetText(video.ChannelName);
        if (video.PublishedAt is { } publishedAt)
        {
            upload_date.SetText(FormatUploadAge(publishedAt, DateTimeOffset.Now));
            upload_date.Visible = true;
        }
        else if (video.ApproximateUploadDate is { } uploadDate)
        {
            upload_date.SetText(FormatUploadAge(uploadDate, DateOnly.FromDateTime(DateTime.Now)));
            upload_date.Visible = true;
        }
        else
        {
            upload_date.SetText(string.Empty);
            upload_date.Visible = false;
        }

        duration.SetText(FormatDuration(video.Duration));
        SetWatchProgress(_watchProgress.GetFraction(video.Id));
        _thumbnailAlternativeText = $"{video.Title} thumbnail";
        menu.TooltipText = $"More actions for {video.Title}";
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        LoadThumbnailAsync(video, generation, _thumbnailCancellation.Token).FireAndForget(Logger);
    }

    public void Unbind()
    {
        _video = null;
        _bindingGeneration++;
        title.SetText(string.Empty);
        title.TooltipText = string.Empty;
        _contextMenu.Popdown();
        channel.SetText(string.Empty);
        duration.SetText(string.Empty);
        upload_date.SetText(string.Empty);
        upload_date.Visible = false;
        SetWatchProgress(null);
        _thumbnailAlternativeText = string.Empty;
        menu.TooltipText = string.Empty;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
    }

    private void OnWatchProgressChanged(object? sender, WatchProgress progress)
    {
        if (_video?.Id != progress.VideoId)
            return;

        Functions.IdleAdd(0, () =>
        {
            if (!_disposed && _video?.Id == progress.VideoId)
                SetWatchProgress(progress.Fraction);
            return false;
        });
    }

    private void SetWatchProgress(double? fraction)
    {
        var isVisible = fraction is > 0;
        watched_progress.SetVisible(isVisible);
        watched_progress.Fraction = isVisible ? fraction!.Value : 0;
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
                () => Pixbuf.NewFromFileAtScale(result.LocalPath, CardWidth, ThumbnailHeight, true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load thumbnail for video {VideoId}", video.Id);
            // A corrupt or unsupported cached image leaves the placeholder intact.
            return;
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Thumbnail decode returned no pixbuf.");

        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || _bindingGeneration != generation ||
                    thumbnail.GetRoot() is null)
                    return false;

                Texture? texture = null;
                Picture? picture = null;
                try
                {
                    var pixbufForTexture = decodedPixbuf ??
                                           throw new InvalidOperationException(
                                               "Thumbnail decode was released before texture creation.");
                    texture = Texture.NewForPixbuf(pixbufForTexture);
                    pixbufForTexture.Dispose();
                    decodedPixbuf = null;
                    picture = Picture.NewForPaintable(texture);
                    picture.AlternativeText = _thumbnailAlternativeText;
                    picture.ContentFit = ContentFit.Cover;
                    picture.WidthRequest = CardWidth;
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
                Logger.Warning(exception, "Failed to render thumbnail texture for video {VideoId}", video.Id);
                // A corrupt or unsupported cached image leaves the placeholder intact.
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
        // A Picture owns a reference to its paintable.  Clear that reference before
        // replacing the child so the previous texture is released on every rebind,
        // rather than waiting for GTK to eventually dispose the detached widget.
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

    private void OnCardReleased(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_video is not { } video)
            return;

        if (sender.GetCurrentButton() == 1)
            StartPlay(video);
        else if (sender.GetCurrentButton() == 2)
            _actions.AddToQueue(video);
    }

    private void OnCardRightClicked(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (_video is null)
            return;

        sender.SetState(EventSequenceState.Claimed);
        var rect = new Rectangle
        {
            X = (int)args.X,
            Y = (int)args.Y,
            Width = 1,
            Height = 1
        };
        _contextMenu.SetPointingTo(rect);
        _contextMenu.Popup();
    }

    private void OnMenuActionActivated(SimpleAction sender, SimpleAction.ActivateSignalArgs args)
    {
        if (ReferenceEquals(sender, _menuActionItems[0]))
        {
            if (_video is { } video)
                StartPlay(video);
        }
        else if (ReferenceEquals(sender, _menuActionItems[1]))
        {
            if (_video is { } video)
                StartAlternatePlay(video);
        }
        else if (ReferenceEquals(sender, _menuActionItems[2]))
        {
            if (_video is { } video)
                _actions.AddToQueue(video);
        }
        else if (ReferenceEquals(sender, _menuActionItems[3]))
        {
            if (_video is not { } video) return;
            if (_actions.OpenChannelAsync is { } openChannel)
                openChannel(video).FireAndForget(Logger);
        }
        else if (ReferenceEquals(sender, _menuActionItems[4]))
        {
            CopyLink();
        }
    }

    private void StartAlternatePlay(VideoSummary video)
    {
        PlayAlternateAsync(video).FireAndForget(Logger);
    }

    private async Task PlayAlternateAsync(VideoSummary video)
    {
        try
        {
            await _actions.OpenInAlternatePlayerAsync(video);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start alternate playback for video {VideoId}", video.Id);
        }
    }

    private void OnChannelReleased(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_video is not { } video)
            return;

        sender.SetState(EventSequenceState.Claimed);
        if (_actions.OpenChannelAsync is { } openChannel)
            openChannel(video).FireAndForget(Logger);
    }

    private static SimpleAction CreateMenuAction(string name)
    {
        return SimpleAction.New(name, null);
    }

    private void StartPlay(VideoSummary video)
    {
        PlayAsync(video).FireAndForget(Logger);
    }

    private async Task PlayAsync(VideoSummary video)
    {
        try
        {
            await _actions.PlayAsync(video);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start playback for video {VideoId}", video.Id);
        }
    }

    private void CopyLink()
    {
        if (_video is not { } video)
            return;

        var link = BuildVideoUrl(video);
        if (link is null)
            return;

        var clipboard = Display.GetDefault()?.GetClipboard();

        clipboard?.SetText(link);
    }

    private static string? BuildVideoUrl(VideoSummary video)
    {
        return string.IsNullOrWhiteSpace(video.WatchUrl)
            ? PlaybackRequest.BuildWatchUrl(video.Id)
            : video.WatchUrl;
    }


    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private static string FormatUploadAge(DateOnly uploadDate, DateOnly today)
    {
        var elapsedDays = Math.Max(0, today.DayNumber - uploadDate.DayNumber);
        return elapsedDays switch
        {
            0 => "Today",
            1 => "1 day ago",
            < 7 => $"{elapsedDays} days ago",
            < 30 => FormatWholeUnits(elapsedDays / 7, "week"),
            < 365 => FormatWholeUnits(elapsedDays / 30, "month"),
            _ => FormatWholeUnits(elapsedDays / 365, "year")
        };
    }

    private static string FormatUploadAge(DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var elapsed = now - publishedAt;
        if (elapsed <= TimeSpan.Zero || elapsed < TimeSpan.FromMinutes(1))
            return "Just now";

        if (elapsed < TimeSpan.FromHours(1))
            return FormatWholeUnits((int)elapsed.TotalMinutes, "minute");

        if (elapsed < TimeSpan.FromDays(1))
            return FormatWholeUnits((int)elapsed.TotalHours, "hour");

        return FormatUploadAge(
            DateOnly.FromDateTime(publishedAt.LocalDateTime),
            DateOnly.FromDateTime(now.LocalDateTime));
    }

    private static string FormatWholeUnits(int count, string unit)
    {
        return count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unbind();

        _click.OnReleased -= OnCardReleased;
        card.RemoveController(_click);
        _click.Dispose();
        _rightClick.OnPressed -= OnCardRightClicked;
        card.RemoveController(_rightClick);
        _rightClick.Dispose();
        _channelClick.OnReleased -= OnChannelReleased;
        channel.RemoveController(_channelClick);
        _watchProgress.ProgressChanged -= OnWatchProgressChanged;
        _channelClick.Dispose();

        _contextMenu.Popdown();
        _contextMenu.Unparent();
        _contextMenu.Dispose();

        menu.MenuModel = null;
        menu.InsertActionGroup("video", null);
        card.InsertActionGroup("video", null);
        foreach (var action in _menuActionItems)
        {
            action.OnActivate -= OnMenuActionActivated;
            _menuActions.RemoveAction(action.Name!);
            action.Dispose();
        }

        _menuActions.Dispose();
        base.Dispose();
        Builder.Dispose();
        Widget.Dispose();
    }
}