using System.Globalization;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Infrastructure.Common;
using Functions = GLib.Functions;

namespace SilverScreen.Player.Controllers;

internal sealed class VideoInfoPanelController : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<VideoInfoPanelController>();

    private readonly Button _backdrop;
    private readonly Label _channelLabel;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly Button _closeButton;
    private readonly Action? _closed;
    private readonly Revealer _cueRevealer;
    private readonly TextView _description;
    private readonly ScrolledWindow _descriptionScroller;
    private readonly Revealer _revealer;
    private readonly Label _statsLabel;
    private readonly Label _statusLabel;
    private readonly Label _titleLabel;
    private readonly IYouTubeVideoDetailsService _videoDetails;

    private bool _bottomEdgeActive;
    private VideoSummary? _currentVideo;
    private bool _disposed;
    private CancellationTokenSource? _infoLoadCancellation;
    private int _infoLoadGeneration;

    public VideoInfoPanelController(
        IYouTubeVideoDetailsService videoDetails,
        Action<VideoSummary> channelRequested,
        Button backdrop,
        Revealer cueRevealer,
        Revealer revealer,
        Label titleLabel,
        Label channelLabel,
        Label statsLabel,
        Label statusLabel,
        ScrolledWindow descriptionScroller,
        TextView description,
        Button closeButton,
        Action? closed = null)
    {
        _videoDetails = videoDetails;
        _channelRequested = channelRequested;
        _backdrop = backdrop;
        _cueRevealer = cueRevealer;
        _revealer = revealer;
        _titleLabel = titleLabel;
        _channelLabel = channelLabel;
        _statsLabel = statsLabel;
        _statusLabel = statusLabel;
        _descriptionScroller = descriptionScroller;
        _description = description;
        _closeButton = closeButton;
        _closed = closed;

        SetInfoContent(null);
    }

    public bool IsOpen { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
    }

    public void Show()
    {
        if (_currentVideo is { } video)
            Show(video);
    }

    private void Show(VideoSummary video)
    {
        if (_disposed) return;
        _currentVideo = video;
        IsOpen = true;
        _bottomEdgeActive = false;
        _cueRevealer.RevealChild = false;
        _backdrop.SetVisible(true);
        _revealer.RevealChild = true;
        _titleLabel.SetText(video.Title);
        _channelLabel.SetText(video.ChannelName);
        _closeButton.GrabFocus();
        _statusLabel.SetText("Loading video details…");
        _statusLabel.SetVisible(true);
        _descriptionScroller.SetVisible(false);
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _infoLoadCancellation = cancellation;
        var generation = ++_infoLoadGeneration;
        LoadVideoInfoAsync(video.Id, generation, cancellation).FireAndForget(Logger);
    }

    public void Close()
    {
        if (!IsOpen && !_revealer.RevealChild) return;
        IsOpen = false;
        ++_infoLoadGeneration;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        _revealer.RevealChild = false;
        _backdrop.SetVisible(false);
        _cueRevealer.RevealChild = false;
        _bottomEdgeActive = false;
        SetInfoContent(null);
        _closed?.Invoke();
    }

    public void Toggle(VideoSummary? video = null)
    {
        if (IsOpen)
            Close();
        else if ((video ?? _currentVideo) is { } targetVideo)
            Show(targetVideo);
    }

    public void SetVideo(VideoSummary? video)
    {
        if (_currentVideo?.Id != video?.Id && IsOpen) Close();

        _currentVideo = video;
        if (!IsOpen) SetInfoContent(null);
    }

    public void UpdatePointer(double x, double y, double width, double height, bool hasMedia)
    {
        if (!hasMedia || IsOpen || width <= 0 || height <= 0)
        {
            if (!_bottomEdgeActive) return;
            _bottomEdgeActive = false;
            _cueRevealer.RevealChild = false;
            return;
        }

        var inZone = PlayerCueGeometry.IsInfoCueActive(x, y, width, height, _bottomEdgeActive);
        if (_bottomEdgeActive == inZone) return;
        _bottomEdgeActive = inZone;
        _cueRevealer.RevealChild = inZone;
    }

    public void OpenChannel()
    {
        if (_currentVideo is not { } video) return;
        Close();
        _channelRequested(video);
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
                _statusLabel.SetText(result.StatusMessage);
                _statusLabel.SetVisible(true);
                _descriptionScroller.SetVisible(false);
            }

            return false;
        });
    }

    private void SetInfoContent(YouTubeVideoDetails? details)
    {
        if (details is null)
        {
            _titleLabel.SetText(_currentVideo?.Title ?? "Video details");
            _channelLabel.SetText(_currentVideo?.ChannelName ?? string.Empty);
            _statsLabel.SetText(string.Empty);
            _statusLabel.SetText("Move to the bottom edge to reveal video details.");
            _statusLabel.SetVisible(true);
            _descriptionScroller.SetVisible(false);
            return;
        }

        _titleLabel.SetText(details.Title);
        _channelLabel.SetText(details.ChannelName);
        _statsLabel.SetText(BuildInfoStats(details));
        _statusLabel.SetVisible(string.IsNullOrWhiteSpace(details.Description));
        _statusLabel.SetText("This video has no description.");
        _descriptionScroller.SetVisible(!string.IsNullOrWhiteSpace(details.Description));
        if (_description.Buffer is { } buffer)
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
}