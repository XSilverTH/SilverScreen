using System.Collections.Concurrent;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
/// Resolves YouTube media streams directly by invoking yt-dlp with <c>--dump-single-json</c>
/// and parsing adaptive/muxed format payloads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architectural Role &amp; Dormant Status:</b><br/>
/// In standard playback, SilverScreen delegates media stream extraction directly to mpv's internal
/// <c>ytdl_hook.lua</c> script, which natively invokes yt-dlp to stream videos directly from YouTube URLs.
/// Consequently, <see cref="ResolveMediaAsync"/> is not currently invoked during the primary playback path.
/// </para>
/// <para>
/// This direct extraction infrastructure is deliberately preserved as:
/// <list type="bullet">
/// <item>
/// <description>
/// A robust fallback pipeline in the event that mpv's internal <c>ytdl_hook.lua</c> fails, experiences
/// compatibility issues with YouTube updates, or requires out-of-process stream resolution.
/// </description>
/// </item>
/// <item>
/// <description>
/// A foundation for features outside the mpv playback engine, such as offline media downloading,
/// headless extraction, stream URL inspection, or custom format muxing.
/// </description>
/// </item>
/// <item>
/// <description>
/// The provider for video metadata via <see cref="GetVideoDetailsAsync"/>, consumed by UI components
/// such as the video info/stats panel.
/// </description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public sealed class YtDlpMediaResolver(
    ICookieFileProvider cookieFileProvider,
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    IYouTubeClientProvider clientProvider,
    TimeSpan? timeout = null,
    TimeSpan? cacheDuration = null,
    TimeProvider? timeProvider = null)
    : IYouTubeMediaResolver, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpMediaResolver>();

    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
    private readonly ConcurrentDictionary<string, CachedVideoEntry> _cache = new(StringComparer.Ordinal);
    private readonly TimeSpan _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(15);

    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new(StringComparer.Ordinal);

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private readonly IYtDlpRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        foreach (var sem in _fetchLocks.Values) sem.Dispose();
        _fetchLocks.Clear();
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

        // Details-only entries carry no yt-dlp payload and must never satisfy media resolve.
        // They are a media-miss here; the fresh fetch below re-resolves via yt-dlp.
        var cached = TryGetValidEntry(videoId, forceRefresh, requireMedia: true);
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
                var media = TrySelectMedia(cached.RawJsonOutput, quality, cached.Details, videoId);
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
            cached = TryGetValidEntry(videoId, forceRefresh, requireMedia: true);
            if (cached is not null)
            {
                if (cached.FormatsByQuality.TryGetValue(quality, out var cachedMedia) &&
                    !IsMediaExpired(cachedMedia.ExpiresAt))
                    return YouTubeMediaResolutionResult.Success(cachedMedia);

                var media = TrySelectMedia(cached.RawJsonOutput, quality, cached.Details, videoId);
                if (media is not null && !IsMediaExpired(media.ExpiresAt))
                {
                    cached.FormatsByQuality[quality] = media;
                    return YouTubeMediaResolutionResult.Success(media);
                }
            }

            var fetchResult = await FetchFromYtDlpAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.IsSuccess || string.IsNullOrWhiteSpace(fetchResult.RawJsonOutput))
                return YouTubeMediaResolutionResult.Failure(fetchResult.ErrorMessage ??
                                                            "Failed to extract video formats.");
            var details = await FetchVideoDetailsFromApiAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!details.IsSuccess || details.Details is null)
                return YouTubeMediaResolutionResult.Failure(details.ErrorMessage ??
                                                            "Failed to load video details.");

            var resolvedMedia = TrySelectMedia(fetchResult.RawJsonOutput, quality, details.Details, videoId);
            if (resolvedMedia is null) return YouTubeMediaResolutionResult.Failure("No suitable media formats found.");

            var newEntry = new CachedVideoEntry(
                fetchResult.RawJsonOutput,
                details.Details,
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
            return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");

        var fetchLock = _fetchLocks.GetOrAdd(videoId, _ => new SemaphoreSlim(1, 1));
        await fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = TryGetValidEntry(videoId, forceRefresh);
            if (cached is not null)
                return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");

            var details = await FetchVideoDetailsFromApiAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!details.IsSuccess || details.Details is null)
                return new YouTubeVideoDetailsResult(null, false,
                    details.ErrorMessage ?? "Failed to load video details.");

            var entry = new CachedVideoEntry(
                string.Empty,
                details.Details,
                _timeProvider.GetUtcNow(),
                null,
                new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase));
            _cache[videoId] = entry;
            return new YouTubeVideoDetailsResult(details.Details, true, "Video details loaded.");
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

    private CachedVideoEntry? TryGetValidEntry(string videoId, bool forceRefresh, bool requireMedia = false)
    {
        if (forceRefresh)
        {
            _cache.TryRemove(videoId, out _);
            return null;
        }

        if (!_cache.TryGetValue(videoId, out var entry)) return null;
        // Details fetch and media resolve share the entry but use independent payloads:
        // a details-only entry has no yt-dlp JSON and must never satisfy media resolve.
        // Report it as a media-miss without evicting so details stay cached.
        if (requireMedia && string.IsNullOrWhiteSpace(entry.RawJsonOutput)) return null;
        var now = _timeProvider.GetUtcNow();
        if (now - entry.CachedAt <= _cacheDuration && !IsMediaExpired(entry.MediaExpiresAt)) return entry;
        _cache.TryRemove(videoId, out _);

        return null;
    }

    // Missing/unparseable media fields are a miss, never a throw: log and let the caller
    // fall through to a fresh fetch (cached path) or return a miss status (fresh path).
    private static ResolvedMedia? TrySelectMedia(
        string rawJson,
        string quality,
        YouTubeVideoDetails? details,
        string videoId)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;
        try
        {
            return YtDlpFormatSelector.SelectMedia(rawJson, quality, details);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Ignoring unparseable media payload for {VideoId}", videoId);
            return null;
        }
    }

    private bool IsMediaExpired(DateTimeOffset? expiresAt)
    {
        if (expiresAt is null) return false;
        var now = _timeProvider.GetUtcNow();
        // Give 30s buffer before actual expiry
        return now >= expiresAt.Value - TimeSpan.FromSeconds(30);
    }

    private async Task<(bool IsSuccess, string? RawJsonOutput, string? ErrorMessage)> FetchFromYtDlpAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        Logger.Information("Extracting media formats for video {VideoId}", videoId);

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        var cookieFilePath = string.IsNullOrWhiteSpace(cookieFile?.Path) ? null : cookieFile.Path;

        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildMediaExtraction(executablePath, videoId, cookieFilePath),
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
            Logger.Warning(ex, "Timeout extracting media for {VideoId}", videoId);
            return (false, null, RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to execute yt-dlp to extract media for {VideoId}", videoId);
            return (false, null, RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(processResult.StandardError))
            {
                Logger.Warning("yt-dlp extraction failed for {VideoId} with exit code {ExitCode}. Stderr: {StdErr}",
                    videoId, processResult.ExitCode, processResult.StandardError.Trim());
            }

            var errorDetail = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? $"the process exited with error code {processResult.ExitCode}: {processResult.StandardError.Trim()}"
                : $"the process exited with error code {processResult.ExitCode}.";

            return (false, null, RuntimeDependencyGuidance.YtDlpFailed(errorDetail));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            if (!string.IsNullOrWhiteSpace(processResult.StandardError))
            {
                Logger.Warning("yt-dlp extraction returned empty output for {VideoId}. Stderr: {StdErr}",
                    videoId, processResult.StandardError.Trim());
            }

            return (false, null, RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));
        }

        return (true, processResult.StandardOutput, null);
    }


    private async Task<(bool IsSuccess, YouTubeVideoDetails? Details, string? ErrorMessage)>
        FetchVideoDetailsFromApiAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            var video = await _clientProvider.GetClient().Videos
                .GetAsync(YoutubeAPI.Models.ValueTypes.VideoId.Parse(videoId), cancellationToken)
                .ConfigureAwait(false);
            var summary = video.Summary;
            return (true, new YouTubeVideoDetails(
                video.Description,
                summary.Statistics.ViewCount,
                summary.PublishedAt,
                summary.Title,
                summary.Channel.Title), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "YoutubeAPI failed to load details for {VideoId}", videoId);
            return (false, null, $"YoutubeAPI could not load video details: {exception.Message}");
        }
    }

    private sealed record CachedVideoEntry(
        string RawJsonOutput,
        YouTubeVideoDetails Details,
        DateTimeOffset CachedAt,
        DateTimeOffset? MediaExpiresAt,
        ConcurrentDictionary<string, ResolvedMedia> FormatsByQuality);
}