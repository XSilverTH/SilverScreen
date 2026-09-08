using Adw;
using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;
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

public partial class VideoCardView : ViewBase<Bin>
{
    private const int CardWidth = 336;
    private const int ThumbnailHeight = 189;
    private static readonly ILogger Logger = Log.ForContext<VideoCardView>();
    private readonly VideoCardActions _actions;
    private readonly PopoverMenu _contextMenu;
    private readonly SimpleAction[] _menuActionItems;
    private readonly SimpleActionGroup _menuActions;
    private readonly IThumbnailService _thumbnails;
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private string _thumbnailAlternativeText = string.Empty;
    private CancellationTokenSource? _thumbnailCancellation;
    private VideoSummary? _video;

    public VideoCardView(IThumbnailService thumbnails, VideoCardActions actions)
    {
        _thumbnails = thumbnails;
        _actions = actions;

        _menuActions = Lifetime.Own(SimpleActionGroup.New());
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
            Lifetime.Own(action);
            Lifetime.Track(
                () => action.OnActivate += OnMenuActionActivated,
                () => action.OnActivate -= OnMenuActionActivated);
            _menuActions.AddAction(action);
        }

        Lifetime.Track(
            () =>
            {
                menu.InsertActionGroup("video", _menuActions);
                card.InsertActionGroup("video", _menuActions);
            },
            () =>
            {
                menu.InsertActionGroup("video", null);
                card.InsertActionGroup("video", null);
            });

        _contextMenu = PopoverMenu.NewFromModel(menu.MenuModel!);
        _contextMenu.HasArrow = false;
        Lifetime.Track(
            () =>
            {
                _contextMenu.SetParent(card);
                _contextMenu.InsertActionGroup("video", _menuActions);
            },
            () =>
            {
                _contextMenu.Popdown();
                _contextMenu.Unparent();
                _contextMenu.InsertActionGroup("video", null);
            });

        var click = GestureClick.New();
        click.Button = 0;
        Lifetime.Attach(card, click,
            c => c.OnReleased += OnCardReleased,
            c => c.OnReleased -= OnCardReleased);

        var rightClick = GestureClick.New();
        rightClick.Button = 3;
        Lifetime.Attach(card, rightClick,
            c => c.OnPressed += OnCardRightClicked,
            c => c.OnPressed -= OnCardRightClicked);

        var channelClick = GestureClick.New();
        channelClick.Button = 1;
        Lifetime.Attach(channel, channelClick,
            c => c.OnReleased += OnChannelReleased,
            c => c.OnReleased -= OnChannelReleased);

        var channelKeyController = EventControllerKey.New();
        Lifetime.Attach(channel, channelKeyController,
            c => c.OnKeyPressed += OnChannelKeyPressed,
            c => c.OnKeyPressed -= OnChannelKeyPressed);

        var keyController = EventControllerKey.New();
        Lifetime.Attach(card, keyController,
            c => c.OnKeyPressed += OnKeyPressed,
            c => c.OnKeyPressed -= OnKeyPressed);
    }

    public void Bind(VideoSummary video, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Unbind();

        _video = video;
        title.SetText(video.Title);
        title.TooltipText = video.Title;
        card.TooltipText = FormatCardTooltip(video);
        channel.SetText(video.ChannelName);
        if (video.PublishedAt is { } publishedAt)
        {
            upload_date.SetText(FormatUploadAge(publishedAt, DateTimeOffset.Now));
            upload_date.TooltipText = FormatTooltipMeta(publishedAt.ToLocalTime().ToString("MMM d, yyyy"));
            upload_date.Visible = true;
        }
        else if (video.ApproximateUploadDate is { } uploadDate)
        {
            upload_date.SetText(FormatUploadAge(uploadDate, DateOnly.FromDateTime(DateTime.Now)));
            upload_date.TooltipText = FormatTooltipMeta(uploadDate.ToString("MMM d, yyyy"));
            upload_date.Visible = true;
        }
        else
        {
            upload_date.SetText(string.Empty);
            upload_date.TooltipText = string.Empty;
            upload_date.Visible = false;
        }

        duration.SetText(FormatDuration(video.Duration));
        _thumbnailAlternativeText = $"{video.Title} thumbnail";
        SetWatchProgress(video.PlaybackProgress?.WatchedFraction);
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
        card.TooltipText = string.Empty;
        _contextMenu.Popdown();
        channel.SetText(string.Empty);
        duration.SetText(string.Empty);
        upload_date.SetText(string.Empty);
        upload_date.TooltipText = string.Empty;
        upload_date.Visible = false;
        SetWatchProgress(null);
        _thumbnailAlternativeText = string.Empty;
        menu.TooltipText = string.Empty;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
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
                if (IsDisposed || cancellationToken.IsCancellationRequested || _bindingGeneration != generation)
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

    private void ShowContextMenu(int x = -1, int y = -1)
    {
        if (IsDisposed || _video is null)
            return;

        var rect = new Rectangle
        {
            X = x >= 0 ? x : Math.Max(0, card.GetWidth() / 2),
            Y = y >= 0 ? y : Math.Max(0, card.GetHeight() / 2),
            Width = 1,
            Height = 1
        };
        _contextMenu.SetPointingTo(rect);
        _contextMenu.Popup();
    }

    private void OnCardRightClicked(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (_video is null)
            return;

        sender.SetState(EventSequenceState.Claimed);
        ShowContextMenu((int)args.X, (int)args.Y);
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (IsDisposed || _video is null)
            return false;

        switch (args.Keyval)
        {
            case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space:
                StartPlay(_video);
                return true;
            case Constants.KEY_Menu:
            case Constants.KEY_F10 when (args.State & ModifierType.ShiftMask) != 0:
                ShowContextMenu();
                return true;
            case Constants.KEY_c or Constants.KEY_C when _actions.OpenChannelAsync is { } openChannel:
                openChannel(_video).FireAndForget(Logger);
                return true;
            default:
                return false;
        }
    }

    private bool OnChannelKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (IsDisposed || _video is null)
            return false;

        switch (args.Keyval)
        {
            case Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space
                when _actions.OpenChannelAsync is { } openChannel:
                openChannel(_video).FireAndForget(Logger);
                return true;
            case Constants.KEY_Menu:
            case Constants.KEY_F10 when (args.State & ModifierType.ShiftMask) != 0:
                ShowContextMenu();
                return true;
            default:
                return false;
        }
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

    private static string FormatViewCount(long views)
    {
        return views switch
        {
            >= 1_000_000_000 => $"{views / 1_000_000_000.0:0.#}B views",
            >= 1_000_000 => $"{views / 1_000_000.0:0.#}M views",
            >= 1_000 => $"{views / 1_000.0:0.#}K views",
            1 => "1 view",
            _ => $"{views:N0} views"
        };
    }

    private static string FormatCardTooltip(VideoSummary video, long? viewCount = null)
    {
        var publishedText = video.PublishedAt is { } publishedAt
            ? publishedAt.ToLocalTime().ToString("MMM d, yyyy")
            : video.ApproximateUploadDate is { } uploadDate
                ? uploadDate.ToString("MMM d, yyyy")
                : null;

        return FormatCardTooltip(video.Title, video.ChannelName, publishedText, viewCount);
    }

    private static string FormatCardTooltip(string title, string channelName, string? publishedText,
        long? viewCount = null)
    {
        var metaLine = FormatTooltipMeta(publishedText, viewCount);

        return string.IsNullOrWhiteSpace(metaLine)
            ? $"{title}\n{channelName}"
            : $"{title}\n{channelName}\n{metaLine}";
    }

    private static string FormatTooltipMeta(string? publishedText, long? viewCount = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(publishedText))
            parts.Add($"Published: {publishedText}");

        if (viewCount is { } count and >= 0)
            parts.Add(FormatViewCount(count));

        return string.Join(" • ", parts);
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