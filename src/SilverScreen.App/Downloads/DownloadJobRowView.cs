using Gdk;
using GdkPixbuf;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Downloads;

public partial class DownloadJobRowView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<DownloadJobRowView>();
    private readonly IDownloadService _downloads;
    private readonly IThumbnailService _thumbnails;
    private DownloadJob? _job;
    private string? _loadedThumbVideoId;
    private Picture? _thumbPicture;
    private Texture? _thumbTexture;

    public DownloadJobRowView(IDownloadService downloads, IThumbnailService thumbnails)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
    }

    public void Bind(DownloadJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _job = job;

        title_label.SetText(job.Video.Title);

        progress_bar.Visible = !job.IsFinished;
        if (job.Status is DownloadJobStatus.Queued or DownloadJobStatus.PostProcessing)
        {
            if (job.Status == DownloadJobStatus.PostProcessing)
                progress_bar.Fraction = 1.0;
            else
                progress_bar.Fraction = job.ProgressFraction;
        }
        else
        {
            progress_bar.Fraction = Math.Clamp(job.ProgressFraction, 0, 1);
        }

        status_label.SetText(FormatStatusLine(job));
        status_label.RemoveCssClass("error");
        if (job.Status == DownloadJobStatus.Failed)
            status_label.AddCssClass("error");

        pause_button.Visible = job.CanPause;
        resume_button.Visible = job.CanResume;
        retry_button.Visible = job.CanRetry;
        cancel_button.Visible = job.CanCancel;
        remove_button.Visible = job.IsFinished;

        if (!string.Equals(_loadedThumbVideoId, job.Video.Id, StringComparison.Ordinal))
        {
            _loadedThumbVideoId = job.Video.Id;
            LoadThumbnailAsync(job.Video).FireAndForget(Logger);
        }
    }

    private async Task LoadThumbnailAsync(VideoSummary video)
    {
        try
        {
            var result = await _thumbnails.GetThumbnailAsync(video).ConfigureAwait(false);
            if (result is null || IsDisposed || !string.Equals(_loadedThumbVideoId, video.Id, StringComparison.Ordinal))
                return;

            var pixbuf = await Task.Run(() => Pixbuf.NewFromFileAtScale(result.LocalPath, 96, 54, true))
                .ConfigureAwait(false);
            if (pixbuf is null) return;

            GtkUiDispatch.Run(
                () => !IsDisposed && string.Equals(_loadedThumbVideoId, video.Id, StringComparison.Ordinal), () =>
                {
                    try
                    {
                        var texture = Texture.NewForPixbuf(pixbuf);
                        if (_thumbPicture is null)
                        {
                            _thumbPicture = Picture.NewForPaintable(texture);
                            _thumbPicture.ContentFit = ContentFit.Cover;
                            _thumbPicture.WidthRequest = 96;
                            _thumbPicture.HeightRequest = 54;
                            thumb_frame.Remove(placeholder);
                            thumb_frame.Append(_thumbPicture);
                        }
                        else
                        {
                            _thumbPicture.Paintable = texture;
                        }

                        _thumbTexture?.Dispose();
                        _thumbTexture = texture;
                    }
                    finally
                    {
                        pixbuf.Dispose();
                    }
                });
        }
        catch
        {
        }
    }

    private static string FormatStatusLine(DownloadJob job)
    {
        switch (job.Status)
        {
            case DownloadJobStatus.Queued:
                return "Queued";
            case DownloadJobStatus.Paused:
                return job.DownloadedBytes is > 0 && job.TotalBytes is > 0
                    ? $"Paused · {job.ProgressFraction * 100:0.#}% ({DownloadActionHelper.FormatBytes(job.DownloadedBytes.Value)} / {DownloadActionHelper.FormatBytes(job.TotalBytes.Value)})"
                    : "Paused";
            case DownloadJobStatus.PostProcessing:
                return "Processing media & metadata…";
            case DownloadJobStatus.Completed:
                return job.Options.DestinationKind == DownloadDestinationKind.Library
                    ? "Saved to Library"
                    : $"Saved to {job.TargetDirectory}";
            case DownloadJobStatus.Failed:
                return string.IsNullOrWhiteSpace(job.ErrorMessage) ? "Download failed" : $"Failed: {job.ErrorMessage}";
            case DownloadJobStatus.Canceled:
                return "Canceled";
            default:
                var parts = new List<string>(4);
                if (job.TotalBytes is > 0 && job.DownloadedBytes is >= 0)
                {
                    parts.Add($"{job.ProgressFraction * 100:0.#}%");
                    parts.Add(
                        $"{DownloadActionHelper.FormatBytes(job.DownloadedBytes.Value)} / {DownloadActionHelper.FormatBytes(job.TotalBytes.Value)}");
                }
                else if (job.DownloadedBytes is > 0)
                {
                    parts.Add(DownloadActionHelper.FormatBytes(job.DownloadedBytes.Value));
                }
                else
                {
                    parts.Add(job.StatusText ?? "Starting…");
                }

                if (job.SpeedBytesPerSecond is > 0)
                    parts.Add(DownloadActionHelper.FormatSpeed(job.SpeedBytesPerSecond.Value));
                if (job.Eta is { } eta)
                    parts.Add($"ETA {DownloadActionHelper.FormatEta(eta)}");

                return string.Join(" · ", parts);
        }
    }

    private void OnPauseClicked(object? sender, EventArgs args)
    {
        if (_job is { } job) _downloads.PauseAsync(job.Id).FireAndForget(Logger);
    }

    private void OnResumeClicked(object? sender, EventArgs args)
    {
        if (_job is { } job) _downloads.ResumeAsync(job.Id).FireAndForget(Logger);
    }

    private void OnRetryClicked(object? sender, EventArgs args)
    {
        if (_job is { } job) _downloads.RetryAsync(job.Id).FireAndForget(Logger);
    }

    private void OnCancelClicked(object? sender, EventArgs args)
    {
        if (_job is { } job) _downloads.CancelAsync(job.Id).FireAndForget(Logger);
    }

    private void OnRemoveClicked(object? sender, EventArgs args)
    {
        if (_job is { } job) _downloads.RemoveAsync(job.Id).FireAndForget(Logger);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _thumbTexture?.Dispose();
            _thumbTexture = null;
            _thumbPicture?.Dispose();
            _thumbPicture = null;
        }

        base.Dispose(disposing);
    }
}