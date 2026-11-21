using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Components;

public partial class VideoCardThumbnailView : ViewBase<Overlay>
{
    private const int CardWidth = 336, ThumbnailHeight = 189;
    private static readonly ILogger Logger = Log.ForContext<VideoCardThumbnailView>();
    private readonly IThumbnailService _thumbnails;
    private readonly NativeMemoryPressure _thumbnailPressure = new();
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private CancellationTokenSource? _thumbnailCancellation;
    private string _thumbnailAlternativeText = string.Empty;

    public VideoCardThumbnailView(IThumbnailService thumbnails) =>
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));

    public void Bind(VideoSummary video, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Unbind();
        duration.SetText(VideoCardFormatting.FormatDuration(video.Duration));
        _thumbnailAlternativeText = $"{video.Title} thumbnail";
        SetWatchProgress(video.PlaybackProgress?.WatchedFraction);
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct) : new();
        LoadThumbnailAsync(video, generation, _thumbnailCancellation.Token).FireAndForget(Logger);
    }

    public void Unbind()
    {
        _bindingGeneration++;
        duration.SetText(string.Empty);
        SetWatchProgress(null);
        _thumbnailAlternativeText = string.Empty;
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation?.Dispose(); _thumbnailCancellation = null;
        ClearThumbnail();
    }

    private void SetWatchProgress(double? fraction)
    {
        watched_progress.SetVisible(fraction is > 0);
        watched_progress.Fraction = fraction is > 0 ? fraction!.Value : 0;
    }

    private async Task LoadThumbnailAsync(VideoSummary video, int generation, CancellationToken ct)
    {
        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(video, ct).ConfigureAwait(false);
            if (result is null) return;
            pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, CardWidth, ThumbnailHeight, true), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Logger.Warning(ex, "Failed to load thumbnail for video"); return; }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Thumbnail decode returned no pixbuf.");
        Functions.IdleAdd(0, () =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation) return false;
                var texture = Texture.NewForPixbuf(decodedPixbuf);
                if (_boundPicture is null)
                {
                    _boundPicture = Picture.NewForPaintable(texture);
                    _boundPicture.ContentFit = ContentFit.Cover;
                    _boundPicture.HeightRequest = ThumbnailHeight;
                    _boundPicture.Hexpand = _boundPicture.Vexpand = true;
                }
                else _boundPicture.Paintable = texture;
                _boundPicture.AlternativeText = _thumbnailAlternativeText;
                Widget.Child = _boundPicture;
                _boundTexture?.Dispose();
                _boundTexture = texture;
                _thumbnailPressure.Track((long)CardWidth * ThumbnailHeight * 4);
            }
            catch (Exception ex) { Logger.Warning(ex, "Failed to render thumbnail texture for video"); }
            finally { decodedPixbuf.Dispose(); }
            return false;
        });
    }

    private void ClearThumbnail()
    {
        var texture = _boundTexture;
        _boundTexture = null;
        _thumbnailPressure.Release();
        Widget.Child = placeholder;
        _boundPicture?.Paintable = null!;
        texture?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Unbind(); _boundPicture?.Dispose(); _boundPicture = null; _thumbnailPressure.Dispose(); }
        base.Dispose(disposing);
    }
}
