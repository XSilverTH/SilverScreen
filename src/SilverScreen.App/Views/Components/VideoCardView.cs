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

    private readonly VideoSummary _video;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _actions;

    public VideoCardView(VideoSummary video, IThumbnailService thumbnails, VideoCardActions actions,
        CancellationToken thumbnailCancellation)
    {
        _video = video;
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

        var thumbnail = CreateThumbnail(out var placeholder);
        card.Append(thumbnail);
        _ = LoadThumbnailAsync(thumbnail, placeholder, thumbnailCancellation);

        var metadata = Box.New(Orientation.Horizontal, 8);
        metadata.CssClasses = ["video-metadata"];
        metadata.MarginStart = 12;
        metadata.MarginEnd = 8;
        metadata.MarginTop = 9;
        metadata.MarginBottom = 10;

        var text = Box.New(Orientation.Vertical, 2);
        text.Hexpand = true;

        var title = Label.New(video.Title);
        title.Xalign = 0;
        title.Wrap = true;
        title.MaxWidthChars = 30;
        title.HeightRequest = 38;
        title.CssClasses = ["video-title"];
        text.Append(title);

        var channel = DimLabel($"{video.ChannelName} • {FormatDuration(video.Duration)}");
        channel.Xalign = 0;
        channel.Ellipsize = Pango.EllipsizeMode.End;
        text.Append(channel);

        var availability = DimLabel(BuildVideoUrl(video) is null
            ? "Mock placeholder • no playable URL"
            : "Playable YouTube URL");
        availability.Xalign = 0;
        availability.Ellipsize = Pango.EllipsizeMode.End;
        text.Append(availability);

        metadata.Append(text);
        metadata.Append(CreateMenuButton());
        card.Append(metadata);

        var click = GestureClick.New();
        click.Button = 0;
        click.OnReleased += (sender, args) =>
        {
            if (sender.GetCurrentButton() == 1)
                _ = PlayAsync();
            else if (sender.GetCurrentButton() == 2)
                _actions.AddToQueue(_video);
        };
        card.AddController(click);
    }

    private Overlay CreateThumbnail(out Widget placeholder)
    {
        var thumbnail = Overlay.New();
        thumbnail.HeightRequest = ThumbnailHeight;
        thumbnail.WidthRequest = CardWidth;
        thumbnail.Hexpand = true;
        thumbnail.Overflow = Overflow.Hidden;
        thumbnail.CssClasses = ["video-thumbnail"];

        var icon = Image.NewFromIconName("media-playback-start-symbolic");
        icon.PixelSize = 44;
        icon.Halign = Align.Center;
        icon.Valign = Align.Center;
        placeholder = icon;
        thumbnail.Child = icon;

        var duration = Label.New(FormatDuration(_video.Duration));
        duration.Halign = Align.End;
        duration.Valign = Align.End;
        duration.MarginEnd = 10;
        duration.MarginBottom = 9;
        duration.CssClasses = ["caption", "duration-pill"];
        thumbnail.AddOverlay(duration);
        return thumbnail;
    }

    private async Task LoadThumbnailAsync(Overlay thumbnail, Widget placeholder, CancellationToken cancellationToken)
    {
        ThumbnailResult? result;
        try
        {
            result = await _thumbnails.GetThumbnailAsync(_video, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (result is null)
            return;

        GLib.Functions.IdleAdd(0, () =>
        {
            if (cancellationToken.IsCancellationRequested || thumbnail.GetRoot() is null ||
                placeholder.GetParent() != thumbnail)
                return false;

            try
            {
                var pixbuf = GdkPixbuf.Pixbuf.NewFromFileAtScale(result.LocalPath, CardWidth, ThumbnailHeight, true);
                var picture = Picture.NewForPixbuf(pixbuf);
                picture.AlternativeText = $"{_video.Title} thumbnail";
                picture.ContentFit = ContentFit.Cover;
                picture.WidthRequest = CardWidth;
                picture.HeightRequest = ThumbnailHeight;
                picture.Hexpand = true;
                picture.Vexpand = true;
                thumbnail.Child = picture;
            }
            catch (Exception)
            {
                // A corrupt or unsupported cached image leaves the placeholder intact.
            }

            return false;
        });
    }

    private MenuButton CreateMenuButton()
    {
        var menu = MenuButton.New();
        menu.IconName = "view-more-symbolic";
        menu.TooltipText = $"More actions for {_video.Title}";
        menu.Valign = Align.Start;
        menu.CssClasses = ["flat"];
        var content = Box.New(Orientation.Vertical, 4);
        content.MarginTop = 6;
        content.MarginBottom = 6;
        content.MarginStart = 6;
        content.MarginEnd = 6;
        content.Append(ActionButton("Play", () => _ = PlayAsync()));
        content.Append(ActionButton("Add to queue", () => _actions.AddToQueue(_video)));
        content.Append(ActionButton("Add next", () => _actions.AddNext(_video)));
        content.Append(
            ActionButton("Open channel", () => _actions.ReportStatus("Opening channels is not implemented.")));
        content.Append(ActionButton("Copy link", CopyLink));
        var popover = Popover.New();
        popover.Child = content;
        menu.Popover = popover;
        return menu;
    }

    private async Task PlayAsync()
    {
        try
        {
            await _actions.PlayAsync(_video);
        }
        catch (Exception)
        {
            _actions.ReportStatus("Playback could not be started.");
        }
    }

    private void CopyLink()
    {
        var link = BuildVideoUrl(_video);
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
}