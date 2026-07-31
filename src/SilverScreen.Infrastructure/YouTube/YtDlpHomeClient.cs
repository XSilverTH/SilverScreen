using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpHomeClient(
    ISessionService sessionService,
    ICookieFileProvider cookieFileProvider,
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    TimeSpan? timeout = null)
    : IYouTubeHomeClient
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpHomeClient>();

    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private readonly IYtDlpRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 20;
        var startIndex = 1;
        if (!string.IsNullOrEmpty(continuationToken) &&
            (!int.TryParse(continuationToken, out startIndex) || startIndex < 1))
            return new HomeFeedResult([], null, true, "Invalid recommendation continuation.", false);

        Logger.Information("Fetching YouTube home feed starting at index {StartIndex}", startIndex);

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
            return new HomeFeedResult(
                [],
                null,
                false,
                "Authentication session not found.",
                true);

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
            return new HomeFeedResult(
                [],
                null,
                false,
                "Failed to create temporary cookie lease.",
                true);

        var (firstResult, firstVideos) =
            await ExecuteYtDlpAsync(executablePath, cookieFile.Path, startIndex, cancellationToken).ConfigureAwait(false);

        if (!firstResult.IsSuccess || firstVideos.Count > 0)
            return firstResult;

        Logger.Information("Authenticated home feed returned 0 videos; retrying without cookies for public recommendations");
        var (retryResult, retryVideos) =
            await ExecuteYtDlpAsync(executablePath, null, startIndex, cancellationToken).ConfigureAwait(false);
        if (!retryResult.IsSuccess)
            return retryResult;
        return new HomeFeedResult(
            retryVideos,
            GetNextContinuationToken(startIndex, retryVideos.Count, pageSize),
            true,
            "Public recommendations are displayed.",
            false);
    }

    private async Task<(HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos)> ExecuteYtDlpAsync(
        string executablePath, string? cookieFilePath, int startIndex, CancellationToken cancellationToken)
    {
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildHome(executablePath, startIndex, cookieFilePath),
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
            var pageEntries = YtDlpVideoParser.Parse(processResult.StandardOutput).ToArray();
            var videos = pageEntries
                .Where(video => !video.IsShort)
                .ToArray();
            return (new HomeFeedResult(
                videos,
                GetNextContinuationToken(startIndex, pageEntries.Length, pageSize: 20),
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

    private static string? GetNextContinuationToken(int startIndex, int resultCount, int pageSize)
    {
        return resultCount == pageSize
            ? (startIndex + pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }
    private static (HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos) Failure(string message)
    {
        return (new HomeFeedResult([], null, false, message, false), []);
    }
}