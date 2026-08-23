using System.Globalization;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Player;

public partial class VideoInfoPanelView : ViewBase<Overlay>
{
    private static readonly ILogger Logger = Log.ForContext<VideoInfoPanelView>();
    private readonly Action<VideoSummary> _channelRequested;
    private readonly Action? _closed;


    private readonly IYouTubeVideoDetailsService _videoDetails;
    private bool _bottomEdgeActive;

    private VideoSummary? _currentVideo;
    private bool _disposed;
    private CancellationTokenSource? _infoLoadCancellation;
    private int _infoLoadGeneration;

    public VideoInfoPanelView(
        IYouTubeVideoDetailsService videoDetails,
        Action<VideoSummary> channelRequested,
        Action? closed = null)
    {
        _videoDetails = videoDetails;
        _channelRequested = channelRequested;
        _closed = closed;

        SetInfoContent(null);
    }

    public bool IsOpen { get; private set; }

    private void Show(VideoSummary video)
    {
        if (_disposed) return;
        _currentVideo = video;
        IsOpen = true;
        _bottomEdgeActive = false;
        info_cue_button.SetVisible(false);
        info_backdrop.SetVisible(true);
        info_revealer.RevealChild = true;
        info_title_label.SetText(video.Title);
        info_channel_label.SetText(video.ChannelName);
        info_close_button.GrabFocus();
        info_status_label.SetText("Loading video details…");
        info_status_label.SetVisible(true);
        info_description_scroller.SetVisible(false);
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _infoLoadCancellation = cancellation;
        var generation = ++_infoLoadGeneration;
        LoadVideoInfoAsync(video.Id, generation, cancellation).FireAndForget(Logger);
    }

    public void Close()
    {
        if (!IsOpen && !info_revealer.RevealChild) return;
        IsOpen = false;
        ++_infoLoadGeneration;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        info_revealer.RevealChild = false;
        info_backdrop.SetVisible(false);
        info_cue_button.SetVisible(false);
        SetInfoContent(null);
        _closed?.Invoke();
    }

    public void Toggle(VideoSummary? video)
    {
        if (IsOpen)
            Close();
        else if (video is not null) Show(video);
    }

    public void SetVideo(VideoSummary? video)
    {
        if (_currentVideo?.Id != video?.Id && IsOpen) Close();

        _currentVideo = video;
        if (!IsOpen) SetInfoContent(null);
    }

    public void UpdatePointer(double y, double height, bool hasMedia)
    {
        var atBottomEdge = hasMedia && !IsOpen && height > 0 && y >= height - 28;
        if (_bottomEdgeActive == atBottomEdge) return;
        _bottomEdgeActive = atBottomEdge;
        info_cue_button.SetVisible(atBottomEdge);
    }

    public void Reset()
    {
        Close();
        _currentVideo = null;
    }

    private void OnBackdropClicked(object? sender, EventArgs args)
    {
        Close();
    }

    private void OnCueButtonClicked(object? sender, EventArgs args)
    {
        if (_currentVideo is { } video) Show(video);
    }

    private void OnChannelButtonClicked(object? sender, EventArgs args)
    {
        if (_currentVideo is not { } video) return;
        Close();
        _channelRequested(video);
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        Close();
    }

    private async Task LoadVideoInfoAsync(string videoId, int generation, CancellationTokenSource cancellation)
    {
        YouTubeVideoDetailsResult result;
        try
        {
            result = await _videoDetails.GetDetailsAsync(videoId, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load video details for {VideoId}", videoId);
            result = new YouTubeVideoDetailsResult(null, false, "Video details could not be loaded.");
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || !IsOpen || generation != _infoLoadGeneration ||
                _currentVideo?.Id != videoId)
                return false;

            if (result is { IsSuccess: true, Details: { } details })
            {
                SetInfoContent(details);
            }
            else
            {
                info_status_label.SetText(result.StatusMessage);
                info_status_label.SetVisible(true);
                info_description_scroller.SetVisible(false);
            }

            return false;
        });
    }

    private void SetInfoContent(YouTubeVideoDetails? details)
    {
        if (details is null)
        {
            info_title_label.SetText(_currentVideo?.Title ?? "Video details");
            info_channel_label.SetText(_currentVideo?.ChannelName ?? string.Empty);
            info_stats_label.SetText(string.Empty);
            info_status_label.SetText("Move to the bottom edge to reveal video details.");
            info_status_label.SetVisible(true);
            info_description_scroller.SetVisible(false);
            return;
        }

        info_title_label.SetText(details.Title);
        info_channel_label.SetText(details.ChannelName);
        info_stats_label.SetText(BuildInfoStats(details));
        info_status_label.SetVisible(string.IsNullOrWhiteSpace(details.Description));
        info_status_label.SetText("This video has no description.");
        info_description_scroller.SetVisible(!string.IsNullOrWhiteSpace(details.Description));
        if (info_description.Buffer is { } buffer)
            buffer.Text = details.Description ?? string.Empty;
    }

    private static string BuildInfoStats(YouTubeVideoDetails details)
    {
        var parts = new List<string>();
        if (details.ViewCount is { } viewCount and >= 0)
            parts.Add($"{viewCount.ToString("N0", CultureInfo.CurrentCulture)} views");
        if (details.PublishedAt is { } publishedAt)
            parts.Add($"Published {publishedAt.ToLocalTime():d}");
        return string.Join(" · ", parts);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        base.Dispose(disposing);
    }
}