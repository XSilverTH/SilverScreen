using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Components;

public sealed class VideoCardActions
{
    public required Func<VideoSummary, Task> PlayAsync { get; init; }
    public required Action<VideoSummary> AddToQueue { get; init; }
    public required Action<VideoSummary> AddNext { get; init; }
    public required Action<string> ReportStatus { get; init; }
}

public partial class VideoCardView : ViewBase<Box>
{
    private const int CardWidth = 336;
    private const int ThumbnailHeight = 189;

    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _actions;
    private readonly Overlay _thumbnail;
    private readonly Widget _placeholder;
    private readonly Label _duration;
    private readonly Label _title;
    private readonly Label _channel;
    private readonly MenuButton _menu;
    private VideoSummary? _video;
    private CancellationTokenSource? _thumbnailCancellation;
    private Gdk.Texture? _boundTexture;
    private Picture? _boundPicture;
    private string _thumbnailAlternativeText = string.Empty;
    private int _bindingGeneration;
    private bool _disposed;

    public VideoCardView(IThumbnailService thumbnails, VideoCardActions actions)
    {
        _thumbnails = thumbnails;
        _actions = actions;

        Widget.WidthRequest = CardWidth;
        Widget.Halign = Align.Center;
        Widget.Valign = Align.Start;
        Widget.Hexpand = false;

        var card = Box.New(Orientation.Vertical, 0);
        card.WidthRequest = CardWidth;
        card.Halign = Align.Center;
        card.Valign = Align.Start;
        card.CssClasses = ["video-card"];
        card.Overflow = Overflow.Hidden;
        Widget.Append(card);

        _thumbnail = Overlay.New();
        _thumbnail.HeightRequest = ThumbnailHeight;
        _thumbnail.WidthRequest = CardWidth;
        _thumbnail.Hexpand = true;
        _thumbnail.Overflow = Overflow.Hidden;
        _thumbnail.CssClasses = ["video-thumbnail"];

        var icon = Image.NewFromIconName("media-playback-start-symbolic");
        icon.PixelSize = 44;
        icon.Halign = Align.Center;
        icon.Valign = Align.Center;
        _placeholder = icon;
        _thumbnail.Child = _placeholder;

        _duration = Label.New(string.Empty);
        _duration.Halign = Align.End;
        _duration.Valign = Align.End;
        _duration.MarginEnd = 10;
        _duration.MarginBottom = 9;
        _duration.CssClasses = ["caption", "duration-pill"];
        _thumbnail.AddOverlay(_duration);
        card.Append(_thumbnail);

        var metadata = Box.New(Orientation.Horizontal, 8);
        metadata.CssClasses = ["video-metadata"];
        metadata.MarginStart = 12;
        metadata.MarginEnd = 8;
        metadata.MarginTop = 9;
        metadata.MarginBottom = 10;

        var text = Box.New(Orientation.Vertical, 2);
        text.Hexpand = true;

        _title = Label.New(string.Empty);
        _title.Xalign = 0;
        _title.Wrap = true;
        _title.MaxWidthChars = 30;
        _title.HeightRequest = 38;
        _title.CssClasses = ["video-title"];
        text.Append(_title);

        _channel = DimLabel(string.Empty);
        _channel.Xalign = 0;
        _channel.Ellipsize = Pango.EllipsizeMode.End;
        text.Append(_channel);

        metadata.Append(text);
        _menu = CreateMenuButton();
        metadata.Append(_menu);
        card.Append(metadata);

        var click = GestureClick.New();
        click.Button = 0;
        click.OnReleased += (sender, args) =>
        {
            if (_video is not { } video)
                return;

            if (sender.GetCurrentButton() == 1)
                StartPlay(video);
            else if (sender.GetCurrentButton() == 2)
                _actions.AddToQueue(video);
        };
        card.AddController(click);
    }

    public void Bind(VideoSummary video, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unbind();

        _video = video;
        _title.SetText(video.Title);
        _channel.SetText($"{video.ChannelName} • {FormatDuration(video.Duration)}");
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
        _thumbnailAlternativeText = string.Empty;
        _menu.TooltipText = string.Empty;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
        ClearThumbnail();
    }

    private async Task LoadThumbnailAsync(VideoSummary video, int generation, CancellationToken cancellationToken)
    {
        GdkPixbuf.Pixbuf? pixbuf = null;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(video, cancellationToken).ConfigureAwait(false);
            if (result is null)
                return;

            pixbuf = await Task.Run(
                () => GdkPixbuf.Pixbuf.NewFromFileAtScale(result.LocalPath, CardWidth, ThumbnailHeight, true),
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

        GdkPixbuf.Pixbuf? decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Thumbnail decode returned no pixbuf.");

        GLib.Functions.IdleAdd(0, () =>
        {
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested || _bindingGeneration != generation ||
                    _thumbnail.GetRoot() is null)
                {
                    return false;
                }

                Gdk.Texture? texture = null;
                Picture? picture = null;
                try
                {
                    var pixbufForTexture = decodedPixbuf ??
                        throw new InvalidOperationException("Thumbnail decode was released before texture creation.");
                    texture = Gdk.Texture.NewForPixbuf(pixbufForTexture);
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
        _thumbnail.Child = _placeholder;
        _boundPicture?.Dispose();
        _boundPicture = null;
        _boundTexture?.Dispose();
        _boundTexture = null;
    }

    private MenuButton CreateMenuButton()
    {
        var menu = MenuButton.New();
        menu.IconName = "view-more-symbolic";
        menu.Valign = Align.Start;
        menu.CssClasses = ["flat"];
        var content = Box.New(Orientation.Vertical, 4);
        content.MarginTop = 6;
        content.MarginBottom = 6;
        content.MarginStart = 6;
        content.MarginEnd = 6;
        content.Append(ActionButton("Play", () =>
        {
            if (_video is { } video)
                StartPlay(video);
        }));
        content.Append(ActionButton("Add to queue", () =>
        {
            if (_video is { } video)
                _actions.AddToQueue(video);
        }));
        content.Append(ActionButton("Add next", () =>
        {
            if (_video is { } video)
                _actions.AddNext(video);
        }));
        content.Append(
            ActionButton("Open channel", () => _actions.ReportStatus("Opening channels is not implemented.")));
        content.Append(ActionButton("Copy link", CopyLink));
        var popover = Popover.New();
        popover.Child = content;
        menu.Popover = popover;
        return menu;
    }

    private void StartPlay(VideoSummary video) => _ = PlayAsync(video);

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

        var clipboard = Gdk.Display.GetDefault()?.GetClipboard();
        if (clipboard is null)
        {
            _actions.ReportStatus("Clipboard is unavailable.");
            return;
        }

        clipboard.SetText(link);
        _actions.ReportStatus("Video link copied to the clipboard.");
    }

    private static Button ActionButton(string label, Action action)
    {
        var button = Button.NewWithLabel(label);
        button.Halign = Align.Fill;
        button.CssClasses = ["flat"];
        button.OnClicked += (_, _) => action();
        return button;
    }

    private static Label DimLabel(string text)
    {
        var label = Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.CssClasses = ["dim-label"];
        return label;
    }

    private static string? BuildVideoUrl(VideoSummary video) => string.IsNullOrWhiteSpace(video.WatchUrl)
        ? PlaybackRequest.BuildWatchUrl(video.Id)
        : video.WatchUrl;

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
        : $"{duration.Minutes}:{duration.Seconds:00}";

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unbind();
        base.Dispose();
    }
}