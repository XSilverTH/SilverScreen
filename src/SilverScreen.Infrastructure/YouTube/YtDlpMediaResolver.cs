using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpMediaResolver : IYouTubeMediaResolver, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpMediaResolver>();

    private readonly ICookieFileProvider _cookieFileProvider;
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, CachedVideoEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed record CachedVideoEntry(
        string RawJsonOutput,
        YouTubeVideoDetails Details,
        DateTimeOffset CachedAt,
        DateTimeOffset? MediaExpiresAt,
        ConcurrentDictionary<string, ResolvedMedia> FormatsByQuality);

    public YtDlpMediaResolver(
        ICookieFileProvider cookieFileProvider,
        IPreferencesService preferencesService,
        IYtDlpRunner runner,
        TimeSpan? timeout = null,
        TimeSpan? cacheDuration = null,
        TimeProvider? timeProvider = null)
    {
        _cookieFileProvider = cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(15);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<YouTubeMediaResolutionResult> ResolveMediaAsync(
        string videoId,
        string? preferredQuality = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return YouTubeMediaResolutionResult.Failure("Media is unavailable for this video.");

        var quality = preferredQuality ?? _preferencesService.GetPreferences().VideoQuality;

        var cached = TryGetValidEntry(videoId, forceRefresh);
        if (cached is not null)
        {
            if (cached.FormatsByQuality.TryGetValue(quality, out var cachedMedia))
            {
                if (!IsMediaExpired(cachedMedia.ExpiresAt))
                    return YouTubeMediaResolutionResult.Success(cachedMedia);
            }
            else
            {
                // Format for this quality wasn't selected yet, but raw json is cached and not expired
                var media = YtDlpFormatSelector.SelectMedia(cached.RawJsonOutput, quality);
                if (media is not null && !IsMediaExpired(media.ExpiresAt))
                {
                    cached.FormatsByQuality[quality] = media;
                    return YouTubeMediaResolutionResult.Success(media);
                }
            }
        }

        // Fetch or wait for concurrent fetch
        var fetchLock = _fetchLocks.GetOrAdd(videoId, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double check cache
            cached = TryGetValidEntry(videoId, forceRefresh);
            if (cached is not null)
            {
                if (cached.FormatsByQuality.TryGetValue(quality, out var cachedMedia) && !IsMediaExpired(cachedMedia.ExpiresAt))
                    return YouTubeMediaResolutionResult.Success(cachedMedia);

                var media = YtDlpFormatSelector.SelectMedia(cached.RawJsonOutput, quality);
                if (media is not null && !IsMediaExpired(media.ExpiresAt))
                {
                    cached.FormatsByQuality[quality] = media;
                    return YouTubeMediaResolutionResult.Success(media);
                }
            }

            var fetchResult = await FetchFromYtDlpAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.IsSuccess || string.IsNullOrWhiteSpace(fetchResult.RawJsonOutput) || fetchResult.Details is null)
            {
                return YouTubeMediaResolutionResult.Failure(fetchResult.ErrorMessage ?? "Failed to extract video formats.");
            }

            var resolvedMedia = YtDlpFormatSelector.SelectMedia(fetchResult.RawJsonOutput, quality);
            if (resolvedMedia is null)
            {
                return YouTubeMediaResolutionResult.Failure("No suitable media formats found.");
            }

            var newEntry = new CachedVideoEntry(
                fetchResult.RawJsonOutput,
                fetchResult.Details,
                _timeProvider.GetUtcNow(),
                resolvedMedia.ExpiresAt,
                new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase)
                {
                    [quality] = resolvedMedia
                });

            _cache[videoId] = newEntry;
            return YouTubeMediaResolutionResult.Success(resolvedMedia);
        }
        finally
        {
            fetchLock.Release();
        }
    }

    public async Task<YouTubeVideoDetailsResult> GetVideoDetailsAsync(
        string videoId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return new YouTubeVideoDetailsResult(null, false, "Video details are unavailable for this video.");

        var cached = TryGetValidEntry(videoId, forceRefresh);
        if (cached is not null)
        {
            return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");
        }

        var fetchLock = _fetchLocks.GetOrAdd(videoId, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = TryGetValidEntry(videoId, forceRefresh);
            if (cached is not null)
            {
                return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");
            }

            var fetchResult = await FetchFromYtDlpAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.IsSuccess || string.IsNullOrWhiteSpace(fetchResult.RawJsonOutput) || fetchResult.Details is null)
            {
                return new YouTubeVideoDetailsResult(null, false, fetchResult.ErrorMessage ?? "Failed to extract video details.");
            }

            var quality = _preferencesService.GetPreferences().VideoQuality;
            var media = YtDlpFormatSelector.SelectMedia(fetchResult.RawJsonOutput, quality);

            var newEntry = new CachedVideoEntry(
                fetchResult.RawJsonOutput,
                fetchResult.Details,
                _timeProvider.GetUtcNow(),
                media?.ExpiresAt,
                media is not null
                    ? new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase) { [quality] = media }
                    : new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase));

            _cache[videoId] = newEntry;
            return new YouTubeVideoDetailsResult(fetchResult.Details, true, "Video details loaded.");
        }
        finally
        {
            fetchLock.Release();
        }
    }

    public void Invalidate(string videoId)
    {
        _cache.TryRemove(videoId, out _);
    }

    private CachedVideoEntry? TryGetValidEntry(string videoId, bool forceRefresh)
    {
        if (forceRefresh)
        {
            _cache.TryRemove(videoId, out _);
            return null;
        }

        if (_cache.TryGetValue(videoId, out var entry))
        {
            var now = _timeProvider.GetUtcNow();
            if (now - entry.CachedAt > _cacheDuration)
            {
                _cache.TryRemove(videoId, out _);
                return null;
            }

            if (IsMediaExpired(entry.MediaExpiresAt))
            {
                _cache.TryRemove(videoId, out _);
                return null;
            }

            return entry;
        }

        return null;
    }

    private bool IsMediaExpired(DateTimeOffset? expiresAt)
    {
        if (expiresAt is null) return false;
        var now = _timeProvider.GetUtcNow();
        // Give 30s buffer before actual expiry
        return now >= (expiresAt.Value - TimeSpan.FromSeconds(30));
    }

    private async Task<(bool IsSuccess, string? RawJsonOutput, YouTubeVideoDetails? Details, string? ErrorMessage)> FetchFromYtDlpAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        Logger.Information("Extracting formats and details for video {VideoId}", videoId);

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
            Logger.Warning(ex, "Timeout extracting video for {VideoId}", videoId);
            return (false, null, null, RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to execute yt-dlp to extract video {VideoId}", videoId);
            return (false, null, null, RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
            return (false, null, null, RuntimeDependencyGuidance.YtDlpFailed(
                $"the process exited with error code {processResult.ExitCode}."));
        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            return (false, null, null, RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));

        try
        {
            var details = YtDlpVideoParser.ParseDetails(processResult.StandardOutput);
            return (true, processResult.StandardOutput, details, null);
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to parse video output JSON for {VideoId}", videoId);
            return (false, null, null, RuntimeDependencyGuidance.YtDlpFailed("the video details output could not be read."));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        foreach (var sem in _fetchLocks.Values)
        {
            sem.Dispose();
        }
        _fetchLocks.Clear();
    }
}
