using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpVideoDetailsService(
    ICookieFileProvider cookieFileProvider,
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    TimeSpan? timeout = null)
    : IYouTubeVideoDetailsService
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpVideoDetailsService>();

    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private readonly IYtDlpRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<YouTubeVideoDetailsResult> GetDetailsAsync(string videoId,
        CancellationToken cancellationToken = default)
    {
        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return Failure("Video details are unavailable for this video.");

        Logger.Information("Fetching details for video {VideoId}", videoId);
        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        var cookieFilePath = string.IsNullOrWhiteSpace(cookieFile?.Path) ? null : cookieFile.Path;

        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildVideoDetails(executablePath, videoId, cookieFilePath),
                    _timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            Logger.Warning(ex, "Timeout fetching details for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to execute yt-dlp to fetch details for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
            return Failure(RuntimeDependencyGuidance.YtDlpFailed(
                $"the process exited with error code {processResult.ExitCode}."));
        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));

        try
        {
            var details = YtDlpVideoParser.ParseDetails(processResult.StandardOutput);
            return new YouTubeVideoDetailsResult(details, true, "Video details loaded.");
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to parse video details JSON output for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the video details output could not be read."));
        }
    }

    private static YouTubeVideoDetailsResult Failure(string statusMessage)
    {
        return new YouTubeVideoDetailsResult(null, false, statusMessage);
    }
}