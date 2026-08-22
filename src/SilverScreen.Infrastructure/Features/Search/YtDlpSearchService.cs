using System.ComponentModel;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpSearchService : ISearchService
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpSearchService>();
    private readonly ICookieFileProvider? _cookieFileProvider;
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;

    public YtDlpSearchService(
        IPreferencesService preferencesService,
        IYtDlpRunner runner,
        ICookieFileProvider? cookieFileProvider = null)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _cookieFileProvider = cookieFileProvider;
    }

    public async Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return SearchResultPage.Empty;

        Logger.Information("Searching videos for query {Query} (StartIndex: {StartIndex})", request.Query, request.StartIndex);
        var activeOptions = GetActiveOptions();
        try
        {
            activeOptions = GetActiveOptions();
            using var cookieFile = _cookieFileProvider?.CreateCookieFile();
            var result = await _runner.RunAsync(
                YtDlpCommandBuilder.BuildSearch(request, activeOptions, cookieFile?.Path),
                activeOptions.Timeout, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"yt-dlp exited with code {result.ExitCode}."
                    : result.StandardError.Trim();
                Logger.Warning(
                    "yt-dlp search exited with code {ExitCode}",
                    result.ExitCode);
                return SearchResultPage.Failed($"Search failed: {RuntimeDependencyGuidance.YtDlpFailed(error)}");
            }

            var pageSize = Math.Max(activeOptions.MaxResults, 1);
            var pageEntries = ParseVideos(result.StandardOutput).ToArray();
            var videos = pageEntries
                .Where(video => !video.IsShort)
                .Take(pageSize)
                .ToList();
            var continuationToken = pageEntries.Length == pageSize
                ? (request.StartIndex + pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;

            return videos.Count == 0
                ? new SearchResultPage(videos, "No video results found.")
                : new SearchResultPage(videos,
                    $"Found {videos.Count} video result{(videos.Count == 1 ? string.Empty : "s")}.",
                    ContinuationToken: continuationToken);
        }
        catch (Win32Exception exception)
        {
            Logger.Warning(exception, "yt-dlp is not installed or could not be started for search");
            return SearchResultPage.Failed(
                $"Search failed: {RuntimeDependencyGuidance.YtDlpUnavailable(activeOptions.ExecutablePath)}");
        }
        catch (JsonException exception)
        {
            Logger.Warning(exception, "yt-dlp returned invalid JSON for search");
            return SearchResultPage.Failed($"Search failed: yt-dlp returned invalid JSON ({exception.Message}).");
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "yt-dlp search timed out");
            return SearchResultPage.Failed($"Search failed: {RuntimeDependencyGuidance.YtDlpTimedOut}");
        }
    }

    private YtDlpOptions GetActiveOptions()
    {
        var prefs = _preferencesService.GetPreferences();
        return new YtDlpOptions
        {
            ExecutablePath = prefs.YtDlpExecutablePath,
            MaxResults = prefs.MaxResults
        };
    }

    private static IEnumerable<VideoSummary> ParseVideos(string output)
    {
        return YtDlpVideoParser.Parse(output);
    }
}