using Gdk;
using GdkPixbuf;
using Gio;
using Gtk;
using Pango;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;
using Action = System.Action;
using Task = System.Threading.Tasks.Task;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Components;

public sealed class VideoCardActions
{
    public required Func<VideoSummary, Task> PlayAsync { get; init; }
    public required Action<VideoSummary> AddToQueue { get; init; }
    public required Action<VideoSummary> AddNext { get; init; }
    public required Action<string> ReportStatus { get; init; }
}

public class VideoCardView : ViewBase<Box>
{
    private const int CardWidth = 336;
    private const int ThumbnailHeight = 189;
    private readonly VideoCardActions _actions;
    private readonly Box _card;
    private readonly Label _channel;
    private readonly Label _uploadDate;
    private readonly Label _duration;
    private readonly MenuButton _menu;
    private readonly Widget _placeholder;
    private readonly Overlay _thumbnail;

    private readonly IThumbnailService _thumbnails;
    private readonly Label _title;
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private bool _disposed;
    private string _thumbnailAlternativeText = string.Empty;
    private CancellationTokenSource? _thumbnailCancellation;
    private VideoSummary? _video;

    public VideoCardView(IThumbnailService thumbnails, VideoCardActions actions)
    {
        _thumbnails = thumbnails;
        _actions = actions;

        _card = GetRequiredObject<Box>("card");
        _thumbnail = GetRequiredObject<Overlay>("thumbnail");
        _placeholder = GetRequiredObject<Widget>("placeholder");
        _duration = GetRequiredObject<Label>("duration");
        _title = GetRequiredObject<Label>("title");
        _channel = GetRequiredObject<Label>("channel");
        _uploadDate = GetRequiredObject<Label>("upload_date");
        _menu = GetRequiredObject<MenuButton>("menu");

        SetupMenuActions();

        var click = GestureClick.New();
        click.Button = 0;
        click.OnReleased += (sender, _) =>
        {
            if (_video is not { } video)
                return;

            if (sender.GetCurrentButton() == 1)
                StartPlay(video);
            else if (sender.GetCurrentButton() == 2)
                _actions.AddToQueue(video);
        };
        _card.AddController(click);
    }

    public void Bind(VideoSummary video, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unbind();

        _video = video;
        _title.SetText(video.Title);
        _channel.SetText(video.ChannelName);
        if (video.PublishedAt is { } publishedAt)
        {
            _uploadDate.SetText(FormatUploadAge(publishedAt, DateTimeOffset.Now));
            _uploadDate.Visible = true;
        }
        else if (video.ApproximateUploadDate is { } uploadDate)
        {
            _uploadDate.SetText(FormatUploadAge(uploadDate, DateOnly.FromDateTime(DateTime.Now)));
            _uploadDate.Visible = true;
        }
        else
        {
            _uploadDate.SetText(string.Empty);
            _uploadDate.Visible = false;
        }
        _duration.SetText(FormatDuration(video.Duration));
        _thumbnailAlternativeText = $"{video.Title} thumbnail";
        _menu.TooltipText = $"More actions for {video.Title}";
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _ = LoadThumbnailAsync(video, generation, _thumbnailCancellation.Token);
    }

    public void Unbind()
    {
        _video = null;
        _bindingGeneration++;
        _title.SetText(string.Empty);
        _channel.SetText(string.Empty);
        _duration.SetText(string.Empty);
        _uploadDate.SetText(string.Empty);
        _uploadDate.Visible = false;
        _thumbnailAlternativeText = string.Empty;
        _menu.TooltipText = string.Empty;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
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
        catch (Exception)
        {
            // A corrupt or unsupported cached image leaves the placeholder intact.
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
        _thumbnail.Child = _placeholder;
        if (picture is not null)
        {
            picture.Paintable = null!;
            picture.Dispose();
        }

        texture?.Dispose();
    }

    private void SetupMenuActions()
    {
        var actions = SimpleActionGroup.New();
        actions.AddAction(CreateMenuAction("play", () =>
        {
            if (_video is { } video)
                StartPlay(video);
        }));
        actions.AddAction(CreateMenuAction("add-to-queue", () =>
        {
            if (_video is { } video)
                _actions.AddToQueue(video);
        }));
        actions.AddAction(CreateMenuAction("add-next", () =>
        {
            if (_video is { } video)
                _actions.AddNext(video);
        }));
        actions.AddAction(CreateMenuAction(
            "open-channel", () => _actions.ReportStatus("Opening channels is not implemented.")));
        actions.AddAction(CreateMenuAction("copy-link", CopyLink));
        _menu.InsertActionGroup("video", actions);

        var menuModel = Menu.New();
        menuModel.Append("Play", "video.play");
        menuModel.Append("Add to queue", "video.add-to-queue");
        menuModel.Append("Add next", "video.add-next");
        menuModel.Append("Open channel", "video.open-channel");
        menuModel.Append("Copy link", "video.copy-link");
        _menu.MenuModel = menuModel;
    }

    private void StartPlay(VideoSummary video)
    {
        _ = PlayAsync(video);
    }

    private async Task PlayAsync(VideoSummary video)
    {
        try
        {
            await _actions.PlayAsync(video);
        }
        catch (Exception)
        {
            _actions.ReportStatus("Playback could not be started.");
        }
    }

    private void CopyLink()
    {
        if (_video is not { } video)
            return;

        var link = BuildVideoUrl(video);
        if (link is null)
        {
            _actions.ReportStatus("No playable video link is available.");
            return;
        }

        var clipboard = Display.GetDefault()?.GetClipboard();
        if (clipboard is null)
        {
            _actions.ReportStatus("Clipboard is unavailable.");
            return;
        }

        clipboard.SetText(link);
        _actions.ReportStatus("Video link copied to the clipboard.");
    }

    private static SimpleAction CreateMenuAction(string name, Action activate)
    {
        var action = SimpleAction.New(name, null);
        action.OnActivate += (_, _) => activate();
        return action;
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

    internal static string FormatUploadAge(DateOnly uploadDate, DateOnly today)
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

    internal static string FormatUploadAge(DateTimeOffset publishedAt, DateTimeOffset now)
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
        base.Dispose();
    }
}
