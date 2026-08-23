using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpCommentService(
    ICookieFileProvider cookieFileProvider,
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    TimeSpan? timeout = null)
    : IYouTubeCommentService
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpCommentService>();

    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private readonly IYtDlpRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<YouTubeCommentsResult> GetCommentsAsync(string videoId, YouTubeCommentSort sort,
        CancellationToken cancellationToken = default)
    {
        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return Failure("Comments are unavailable for this video.");

        Logger.Information("Fetching comments for video {VideoId} (Sort: {Sort})", videoId, sort);

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        var cookieFilePath = string.IsNullOrWhiteSpace(cookieFile?.Path) ? null : cookieFile.Path;

        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildComments(executablePath, videoId, sort, cookieFilePath),
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
            Logger.Warning(ex, "Timeout fetching comments for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to execute yt-dlp to fetch comments for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning("yt-dlp comment fetch returned exit code {ExitCode} for video {VideoId}",
                processResult.ExitCode, videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpFailed(
                $"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));

        try
        {
            var comments = ParseComments(processResult.StandardOutput);
            return new YouTubeCommentsResult(
                comments,
                true,
                comments.Count == 0 ? "No comments were returned for this video." : "Comments loaded.");
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to parse comment JSON output for video {VideoId}", videoId);
            return Failure(RuntimeDependencyGuidance.YtDlpFailed("the comment output could not be read."));
        }
    }

    private static List<YouTubeComment> ParseComments(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("yt-dlp comment output must be an object.");

        if (!root.TryGetProperty("comments", out var commentsElement) ||
            commentsElement.ValueKind == JsonValueKind.Null)
            return [];

        if (commentsElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("yt-dlp comments must be an array.");

        var comments = new List<YouTubeComment>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commentElement in commentsElement.EnumerateArray())
        {
            if (commentElement.ValueKind != JsonValueKind.Object) continue;

            var id = GetString(commentElement, "id");
            var text = GetString(commentElement, "text");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text) || !seenIds.Add(id)) continue;

            var authorName = GetString(commentElement, "author");
            if (string.IsNullOrWhiteSpace(authorName)) authorName = "YouTube user";

            comments.Add(new YouTubeComment(
                id,
                authorName,
                text,
                GetString(commentElement, "_time_text") ?? GetString(commentElement, "time_text") ?? string.Empty,
                GetLikeCount(commentElement),
                GetParentId(commentElement)));
        }

        return comments;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetParentId(JsonElement element)
    {
        var parentId = GetString(element, "parent");
        return string.IsNullOrWhiteSpace(parentId) || string.Equals(parentId, "root", StringComparison.Ordinal)
            ? null
            : parentId;
    }

    private static long GetLikeCount(JsonElement element)
    {
        return element.TryGetProperty("like_count", out var value) && value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var likeCount) && likeCount >= 0
            ? likeCount
            : 0;
    }

    private static YouTubeCommentsResult Failure(string statusMessage)
    {
        return new YouTubeCommentsResult([], false, statusMessage);
    }
}