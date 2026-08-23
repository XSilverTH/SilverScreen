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

    [BlueprintWidget]
    private Button _infoBackdrop = null!;

    [BlueprintWidget]
    private Button _infoCueButton = null!;

    [BlueprintWidget]
    private Revealer _infoRevealer = null!;

    [BlueprintWidget]
    private Label _infoTitleLabel = null!;

    [BlueprintWidget]
    private Label _infoChannelLabel = null!;

    [BlueprintWidget]
    private Button _infoCloseButton = null!;

    [BlueprintWidget]
    private Label _infoStatsLabel = null!;

    [BlueprintWidget]
    private Label _infoStatusLabel = null!;

    [BlueprintWidget]
    private ScrolledWindow _infoDescriptionScroller = null!;

    [BlueprintWidget]
    private TextView _infoDescription = null!;

    private readonly IYouTubeVideoDetailsService _videoDetails;
    private readonly Action<VideoSummary> _channelRequested;
    private readonly Action? _closed;

    private VideoSummary? _currentVideo;
    private bool _infoOpen;
    private bool _bottomEdgeActive;
    private int _infoLoadGeneration;
    private CancellationTokenSource? _infoLoadCancellation;
    private bool _disposed;

    public bool IsOpen => _infoOpen;

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

    public void Show(VideoSummary video)
    {
        if (_disposed) return;
        _currentVideo = video;
        _infoOpen = true;
        _bottomEdgeActive = false;
        _infoCueButton.SetVisible(false);
        _infoBackdrop.SetVisible(true);
        _infoRevealer.RevealChild = true;
        _infoTitleLabel.SetText(video.Title);
        _infoChannelLabel.SetText(video.ChannelName);
        _infoCloseButton.GrabFocus();
        _infoStatusLabel.SetText("Loading video details…");
        _infoStatusLabel.SetVisible(true);
        _infoDescriptionScroller.SetVisible(false);
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _infoLoadCancellation = cancellation;
        var generation = ++_infoLoadGeneration;
        LoadVideoInfoAsync(video.Id, generation, cancellation).FireAndForget(Logger);
    }

    public void Close()
    {
        if (!_infoOpen && !_infoRevealer.RevealChild) return;
        _infoOpen = false;
        ++_infoLoadGeneration;
        _infoLoadCancellation?.Cancel();
        _infoLoadCancellation?.Dispose();
        _infoLoadCancellation = null;
        _infoRevealer.RevealChild = false;
        _infoBackdrop.SetVisible(false);
        _infoCueButton.SetVisible(false);
        SetInfoContent(null);
        _closed?.Invoke();
    }

    public void Toggle(VideoSummary? video)
    {
        if (_infoOpen)
        {
            Close();
        }
        else if (video is not null)
        {
            Show(video);
        }
    }

    public void SetVideo(VideoSummary? video)
    {
        if (_currentVideo?.Id != video?.Id && _infoOpen)
        {
            Close();
        }

        _currentVideo = video;
        if (!_infoOpen)
        {
            SetInfoContent(null);
        }
    }

    public void UpdatePointer(double y, double height, bool hasMedia)
    {
        var atBottomEdge = hasMedia && !_infoOpen && height > 0 && y >= height - 28;
        if (_bottomEdgeActive == atBottomEdge) return;
        _bottomEdgeActive = atBottomEdge;
        _infoCueButton.SetVisible(atBottomEdge);
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
        if (_currentVideo is { } video)
        {
            Show(video);
        }
    }

    private void OnChannelButtonClicked(object? sender, EventArgs args)
    {
        if (_currentVideo is { } video)
        {
            Close();
            _channelRequested(video);
        }
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
            if (_disposed || !_infoOpen || generation != _infoLoadGeneration ||
                _currentVideo?.Id != videoId)
                return false;

            if (result is { IsSuccess: true, Details: { } details })
            {
                SetInfoContent(details);
            }
            else
            {
                _infoStatusLabel.SetText(result.StatusMessage);
                _infoStatusLabel.SetVisible(true);
                _infoDescriptionScroller.SetVisible(false);
            }

            return false;
        });
    }

    private void SetInfoContent(YouTubeVideoDetails? details)
    {
        if (details is null)
        {
            _infoTitleLabel.SetText(_currentVideo?.Title ?? "Video details");
            _infoChannelLabel.SetText(_currentVideo?.ChannelName ?? string.Empty);
            _infoStatsLabel.SetText(string.Empty);
            _infoStatusLabel.SetText("Move to the bottom edge to reveal video details.");
            _infoStatusLabel.SetVisible(true);
            _infoDescriptionScroller.SetVisible(false);
            return;
        }

        _infoTitleLabel.SetText(details.Title);
        _infoChannelLabel.SetText(details.ChannelName);
        _infoStatsLabel.SetText(BuildInfoStats(details));
        _infoStatusLabel.SetVisible(string.IsNullOrWhiteSpace(details.Description));
        _infoStatusLabel.SetText("This video has no description.");
        _infoDescriptionScroller.SetVisible(!string.IsNullOrWhiteSpace(details.Description));
        if (_infoDescription.Buffer is { } buffer)
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
