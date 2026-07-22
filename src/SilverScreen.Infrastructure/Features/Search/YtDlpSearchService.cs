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
    private readonly IPreferencesService? _preferencesService;
    private readonly IYtDlpRunner _runner;
    private readonly YtDlpOptions _staticOptions;

    public YtDlpSearchService()
        : this(new YtDlpOptions(), new YtDlpRunner())
    {
    }

    public YtDlpSearchService(YtDlpOptions options, IYtDlpRunner runner)
    {
        _staticOptions = options;
        _runner = runner;
        _preferencesService = null;
    }

    public YtDlpSearchService(IPreferencesService preferencesService, IYtDlpRunner runner)
    {
        _staticOptions = new YtDlpOptions();
        _runner = runner;
        _preferencesService = preferencesService;
    }

    public async Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return SearchResultPage.Empty;

        var activeOptions = _staticOptions;
        try
        {
            activeOptions = GetActiveOptions();
            var result = await _runner.RunSearchAsync(request, activeOptions, cancellationToken).ConfigureAwait(false);
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

            var videos = ParseVideos(result.StandardOutput)
                .Where(video => !video.IsShort)
                .Take(activeOptions.MaxResults)
                .ToList();

            return videos.Count == 0
                ? new SearchResultPage(videos, "No video results found.")
                : new SearchResultPage(videos,
                    $"Found {videos.Count} video result{(videos.Count == 1 ? string.Empty : "s")}.");
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

    public bool IsLikelyYouTubeUrl(string text)
    {
        return YouTubeUrlParser.Parse(text).Kind is not YouTubeUrlKind.NotYouTube and not YouTubeUrlKind.Invalid;
    }

    private YtDlpOptions GetActiveOptions()
    {
        if (_preferencesService is null) return _staticOptions;
        var prefs = _preferencesService.GetPreferences();
        return _staticOptions with
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