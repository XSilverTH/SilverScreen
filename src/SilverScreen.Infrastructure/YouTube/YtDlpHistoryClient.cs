using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Account.Session;
using System.Globalization;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>Reads the signed-in account's YouTube watch history through a temporary cookie lease.</summary>
public sealed class YtDlpHistoryClient(
    ISessionService sessionService,
    ICookieFileProvider cookieFileProvider,
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    TimeSpan? timeout = null) : IYouTubeHistoryClient
{
    private const int PageSize = 20;
    private static readonly ILogger Logger = Log.ForContext<YtDlpHistoryClient>();

    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private readonly IYtDlpRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<HistoryFeedResult> GetHistoryAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var startIndex = 1;
        if (!string.IsNullOrEmpty(continuationToken) &&
            (!int.TryParse(continuationToken, out startIndex) || startIndex < 1))
            return new HistoryFeedResult([], null, false, "Invalid history continuation.", false);

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
            return new HistoryFeedResult([], null, false, "Authentication session not found.", true);

        Logger.Information("Fetching YouTube watch history starting at index {StartIndex}", startIndex);
        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
            return new HistoryFeedResult([], null, false, "Failed to create temporary cookie lease.", true);

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildHistory(executablePath, startIndex, cookieFile.Path),
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
            Logger.Warning(exception, "yt-dlp timed out while loading watch history");
            return Failure(RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for watch history");
            return Failure(RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning("yt-dlp exited with code {ExitCode} while loading watch history", processResult.ExitCode);
            return Failure(RuntimeDependencyGuidance.YtDlpFailed(
                $"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            return new HistoryFeedResult([], null, true, "Watch history loaded.", false);

        try
        {
            var pageEntries = YtDlpVideoParser.Parse(processResult.StandardOutput).ToArray();
            var videos = pageEntries.Where(video => !video.IsShort).ToArray();
            var nextToken = pageEntries.Length == PageSize
                ? (startIndex + PageSize).ToString(CultureInfo.InvariantCulture)
                : null;
            return new HistoryFeedResult(videos, nextToken, true, "Watch history loaded.", false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for watch history");
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the watch history output could not be read."));
        }
    }

    private static HistoryFeedResult Failure(string message)
    {
        return new HistoryFeedResult([], null, false, message, false);
    }
}