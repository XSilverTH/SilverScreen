using System.Globalization;
using Serilog;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Player.RichText;

namespace SilverScreen.Player.Info;

/// <summary>
///     Coordinates asynchronous loading and formatting of video details for the info panel.
/// </summary>
internal sealed class PlayerVideoInfoLoader(IYouTubeMediaResolver mediaResolver) : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlayerVideoInfoLoader>();
    private CancellationTokenSource? _cancellation;
    private int _generation;

    public YouTubeVideoDetails? LoadedDetails { get; private set; }
    public string? LoadedError { get; private set; }

    public void Dispose()
    {
        Reset();
    }

    public void Reset()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        LoadedDetails = null;
        LoadedError = null;
    }

    public async Task<bool> LoadAsync(string videoId, Action onCompleted)
    {
        Reset();
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return false;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var currentGen = ++_generation;

        YouTubeVideoDetailsResult result;
        try
        {
            result = await mediaResolver.GetVideoDetailsAsync(videoId, false, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load video details");
            result = new YouTubeVideoDetailsResult(null, false, "Video details could not be loaded.");
        }

        GtkUiDispatch.Run(() => currentGen == _generation, () =>
        {
            if (result is { IsSuccess: true, Details: { } details })
            {
                LoadedDetails = details;
                LoadedError = null;
            }
            else
            {
                LoadedDetails = null;
                LoadedError = string.IsNullOrWhiteSpace(result.StatusMessage)
                    ? "Video details could not be loaded."
                    : result.StatusMessage;
            }

            onCompleted();
        });

        return true;
    }

    public static string FormatStats(YouTubeVideoDetails details)
    {
        var parts = new List<string>();
        if (details.ViewCount is { } viewCount and >= 0)
            parts.Add($"{viewCount.ToString("N0", CultureInfo.CurrentCulture)} views");
        if (details.PublishedAt is { } publishedAt)
            parts.Add($"Published {publishedAt.ToLocalTime():d}");
        return string.Join(" · ", parts);
    }

    public static string FormatMarkup(string description)
    {
        return RichLabelMarkup.Build(description);
    }
}