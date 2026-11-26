using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Components;

public partial class VideoCardThumbnailView(IThumbnailService thumbnails) : ViewBase<Overlay>
{
    private const int CardWidth = 336, ThumbnailHeight = 189;
    private static readonly ILogger Logger = Log.ForContext<VideoCardThumbnailView>();
    private static readonly SemaphoreSlim DecodeSemaphore = new(4, 4);
    private readonly NativeMemoryPressure _thumbnailPressure = new();
    private readonly IThumbnailService _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
    private int _bindingGeneration;
    private Picture? _boundPicture;
    private Texture? _boundTexture;
    private string? _boundThumbnailUrl;
    private string? _boundVideoId;
    private string _thumbnailAlternativeText = string.Empty;
    private CancellationTokenSource? _thumbnailCancellation;

    public void Bind(VideoSummary video, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(video);

        duration.SetText(VideoCardFormatting.FormatDuration(video.Duration));
        _thumbnailAlternativeText = $"{video.Title} thumbnail";
        SetWatchProgress(video.PlaybackProgress?.WatchedFraction);

        var sameMedia = string.Equals(_boundVideoId, video.Id, StringComparison.Ordinal) &&
                        string.Equals(_boundThumbnailUrl, video.ThumbnailUrl, StringComparison.Ordinal) &&
                        (_boundTexture is not null || _thumbnailCancellation is not null);
        if (sameMedia)
        {
            if (_boundPicture is not null)
                _boundPicture.AlternativeText = _thumbnailAlternativeText;
            return;
        }

        CancelThumbnailLoad();
        ClearThumbnail();

        _boundVideoId = video.Id;
        _boundThumbnailUrl = video.ThumbnailUrl;
        var generation = ++_bindingGeneration;
        _thumbnailCancellation = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        LoadThumbnailAsync(video, generation, _thumbnailCancellation.Token).FireAndForget(Logger);
    }

    public void Unbind()
    {
        _bindingGeneration++;
        _boundVideoId = null;
        _boundThumbnailUrl = null;
        duration.SetText(string.Empty);
        SetWatchProgress(null);
        _thumbnailAlternativeText = string.Empty;
        CancelThumbnailLoad();
        ClearThumbnail();
    }

    private void CancelThumbnailLoad()
    {
        try
        {
            _thumbnailCancellation?.Cancel();
        }
        catch (Exception)
        {
        }

        _thumbnailCancellation?.Dispose();
        _thumbnailCancellation = null;
    }

    private void SetWatchProgress(double? fraction)
    {
        watched_progress.SetVisible(fraction is > 0);
        watched_progress.Fraction = fraction is > 0 ? fraction.Value : 0;
    }

    private async Task LoadThumbnailAsync(VideoSummary video, int generation, CancellationToken ct)
    {
        var acquired = false;
        Pixbuf? pixbuf;
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(video, ct).ConfigureAwait(false);
            if (result is null || IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation)
                return;

            await DecodeSemaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation)
                return;

            pixbuf = await Task
                .Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, CardWidth, ThumbnailHeight, true), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load thumbnail for video");
            return;
        }
        finally
        {
            if (acquired)
                DecodeSemaphore.Release();
        }

        var decodedPixbuf = pixbuf ?? throw new InvalidOperationException("Thumbnail decode returned no pixbuf.");
        GtkUiDispatch.Run(() =>
        {
            try
            {
                if (IsDisposed || ct.IsCancellationRequested || _bindingGeneration != generation) return;
                var texture = Texture.NewForPixbuf(decodedPixbuf);
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

                _boundPicture.AlternativeText = _thumbnailAlternativeText;
                Widget.Child = _boundPicture;
                _boundTexture?.Dispose();
                _boundTexture = texture;
                _thumbnailPressure.Track((long)CardWidth * ThumbnailHeight * 4);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to render thumbnail texture for video");
            }
            finally
            {
                decodedPixbuf.Dispose();
            }
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
        if (disposing)
        {
            Unbind();
            _boundPicture?.Dispose();
            _boundPicture = null;
            _thumbnailPressure.Dispose();
        }

        base.Dispose(disposing);
    }
}
