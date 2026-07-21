using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpHomeClient(
    ISessionService sessionService,
    ICookieFileProvider cookieFileProvider,
    string executablePath = "yt-dlp",
    TimeSpan? timeout = null,
    IYtDlpProcessRunner? processRunner = null)
    : IYouTubeHomeClient
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpHomeClient>();
    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));
    private readonly IYtDlpProcessRunner _processRunner =
        processRunner ?? new YtDlpRunner();
    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(continuationToken))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                true,
                "Continuations are not supported.",
                false);

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                false,
                "Authentication session not found.",
                true);

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                false,
                "Failed to create temporary cookie lease.",
                true);

        var (firstResult, firstVideos) =
            await ExecuteYtDlpAsync(cookieFile.Path, cancellationToken).ConfigureAwait(false);

        if (!firstResult.IsSuccess || firstVideos.Count > 0)
            return firstResult;

        var (retryResult, retryVideos) = await ExecuteYtDlpAsync(null, cancellationToken).ConfigureAwait(false);
        if (!retryResult.IsSuccess)
            return retryResult;

        return new HomeFeedResult(
            retryVideos,
            null,
            true,
            "Public recommendations are displayed.",
            false);
    }

    private async Task<(HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos)> ExecuteYtDlpAsync(
        string? cookieFilePath,
        CancellationToken cancellationToken)
    {
        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                    YtDlpRunner.BuildHomeStartInfo(executablePath, cookieFilePath),
                    _timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "yt-dlp timed out while loading home recommendations");
            return Failure(RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for home recommendations");
            return Failure(RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning(
                "yt-dlp exited with code {ExitCode} while loading home recommendations",
                processResult.ExitCode);
            return Failure(RuntimeDependencyGuidance.YtDlpFailed(
                $"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            Logger.Warning("yt-dlp returned empty output for home recommendations");
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));
        }

        try
        {
            var videos = YtDlpVideoParser.Parse(processResult.StandardOutput)
                .Where(video => !video.IsShort)
                .ToArray();
            return (new HomeFeedResult(
                videos,
                null,
                true,
                "Recommendations loaded successfully.",
                false), videos);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for home recommendations");
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the recommendation output could not be read."));
        }
    }

    private static (HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos) Failure(string message)
    {
        return (new HomeFeedResult([], null, false, message, false), []);
    }
}