using System.Collections.Concurrent;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using YoutubeAPI.Models.ValueTypes;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
///     Resolves YouTube media streams directly by invoking yt-dlp with <c>--dump-single-json</c>
///     and parsing adaptive/muxed format payloads once into compact immutable records.
/// </summary>
public sealed class YtDlpMediaResolver : IYouTubeMediaResolver, IDisposable
{
    internal const int MaxCachedEntries = 50;

    private static readonly ILogger Logger = Log.ForContext<YtDlpMediaResolver>();
    private readonly BoundedLruCache<string, CachedVideoEntry> _cache;
    private readonly TimeSpan _cacheDuration;
    private readonly IYouTubeClientProvider _clientProvider;
    private readonly ICookieFileProvider _cookieFileProvider;
    private readonly Dictionary<string, RefCountedLock> _fetchLocks = new(StringComparer.Ordinal);
    private readonly Lock _fetchLocksGate = new();
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public YtDlpMediaResolver(
        ICookieFileProvider cookieFileProvider,
        IPreferencesService preferencesService,
        IYtDlpRunner runner,
        IYouTubeClientProvider clientProvider,
        TimeSpan? timeout = null,
        TimeSpan? cacheDuration = null,
        TimeProvider? timeProvider = null)
    {
        _cookieFileProvider = cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(15);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cache = new BoundedLruCache<string, CachedVideoEntry>(MaxCachedEntries, StringComparer.Ordinal, _timeProvider);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        lock (_fetchLocksGate)
        {
            foreach (var entry in _fetchLocks.Values)
                entry.Semaphore.Dispose();
            _fetchLocks.Clear();
        }
    }

    public async Task<YouTubeMediaResolutionResult> ResolveMediaAsync(
        string videoId,
        string? preferredQuality = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return new YouTubeMediaResolutionResult.Failure("Media is unavailable for this video.");

        var quality = preferredQuality ?? _preferencesService.GetPreferences().Quality.ToPersistedString();

        var cached = TryGetValidEntry(videoId, forceRefresh, true);
        if (cached is not null)
        {
            if (cached.FormatsByQuality.TryGetValue(quality, out var cachedMedia))
            {
                if (!IsMediaExpired(cachedMedia.ExpiresAt))
                    return new YouTubeMediaResolutionResult.Success(cachedMedia);
            }
            else if (cached.Payload is not null)
            {
                var media = TrySelectMedia(cached.Payload, quality, cached.Details, videoId);
                if (media is not null && !IsMediaExpired(media.ExpiresAt))
                {
                    cached.FormatsByQuality[quality] = media;
                    return new YouTubeMediaResolutionResult.Success(media);
                }
            }
        }

        var fetchLock = AcquireFetchLock(videoId);
        var acquired = false;
        try
        {
            await fetchLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            cached = TryGetValidEntry(videoId, forceRefresh, true);
            if (cached is not null)
            {
                if (cached.FormatsByQuality.TryGetValue(quality, out var cachedMedia) &&
                    !IsMediaExpired(cachedMedia.ExpiresAt))
                    return new YouTubeMediaResolutionResult.Success(cachedMedia);

                if (cached.Payload is not null)
                {
                    var media = TrySelectMedia(cached.Payload, quality, cached.Details, videoId);
                    if (media is not null && !IsMediaExpired(media.ExpiresAt))
                    {
                        cached.FormatsByQuality[quality] = media;
                        return new YouTubeMediaResolutionResult.Success(media);
                    }
                }
            }

            var fetchResult = await FetchFromYtDlpAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.IsSuccess || string.IsNullOrWhiteSpace(fetchResult.RawJsonOutput))
                return new YouTubeMediaResolutionResult.Failure(fetchResult.ErrorMessage ??
                                                                "Failed to extract video formats.");

            var parsed = YtDlpFormatSelector.ParsePayload(fetchResult.RawJsonOutput);
            if (parsed is null)
                return new YouTubeMediaResolutionResult.Failure("No suitable media formats found.");

            var details = parsed.Details;
            if (details is null)
            {
                var apiDetailsResult =
                    await FetchVideoDetailsFromApiAsync(videoId, cancellationToken).ConfigureAwait(false);
                if (apiDetailsResult.IsSuccess)
                    details = apiDetailsResult.Details;
                else
                    Logger.Debug("Continuing media resolution without optional details: {Message}",
                        DiagnosticSanitizer.Sanitize(apiDetailsResult.ErrorMessage));
            }

            var resolvedMedia = TrySelectMedia(parsed, quality, details, videoId);
            if (resolvedMedia is null)
                return new YouTubeMediaResolutionResult.Failure("No suitable media formats found.");

            var newEntry = new CachedVideoEntry(
                parsed,
                details,
                _timeProvider.GetUtcNow(),
                resolvedMedia.ExpiresAt,
                new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase)
                {
                    [quality] = resolvedMedia
                });

            _cache.Set(videoId, newEntry);
            return new YouTubeMediaResolutionResult.Success(resolvedMedia);
        }
        finally
        {
            ReleaseFetchLock(videoId, fetchLock, acquired);
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
        if (cached?.Details is not null)
            return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");

        var fetchLock = AcquireFetchLock(videoId);
        var acquired = false;
        try
        {
            await fetchLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            cached = TryGetValidEntry(videoId, forceRefresh);
            if (cached?.Details is not null)
                return new YouTubeVideoDetailsResult(cached.Details, true, "Video details loaded.");

            if (cached?.Payload?.Details is { } parsedDetails)
            {
                _cache.Set(videoId, cached with { Details = parsedDetails });
                return new YouTubeVideoDetailsResult(parsedDetails, true, "Video details loaded.");
            }

            var details = await FetchVideoDetailsFromApiAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!details.IsSuccess || details.Details is null)
                return new YouTubeVideoDetailsResult(null, false,
                    details.ErrorMessage ?? "Failed to load video details.");

            var entry = cached is not null
                ? cached with { Details = details.Details }
                : new CachedVideoEntry(
                    null,
                    details.Details,
                    _timeProvider.GetUtcNow(),
                    null,
                    new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase));
            _cache.Set(videoId, entry);
            return new YouTubeVideoDetailsResult(details.Details, true, "Video details loaded.");
        }
        finally
        {
            ReleaseFetchLock(videoId, fetchLock, acquired);
        }
    }

    public async Task<IReadOnlyList<string>> GetQualityTracksAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId))
            return [];

        var cached = TryGetValidEntry(videoId, false, true);
        if (cached?.Payload is not null)
            return YtDlpFormatSelector.GetAvailableQualityTracks(cached.Payload);

        var fetchLock = AcquireFetchLock(videoId);
        var acquired = false;
        try
        {
            await fetchLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            cached = TryGetValidEntry(videoId, false, true);
            if (cached?.Payload is not null)
                return YtDlpFormatSelector.GetAvailableQualityTracks(cached.Payload);

            var fetchResult = await FetchFromYtDlpAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!fetchResult.IsSuccess || string.IsNullOrWhiteSpace(fetchResult.RawJsonOutput))
                return [];

            var parsed = YtDlpFormatSelector.ParsePayload(fetchResult.RawJsonOutput);
            if (parsed is null)
                return [];

            var details = parsed.Details;
            var quality = _preferencesService.GetPreferences().Quality.ToPersistedString();
            var resolvedMedia = TrySelectMedia(parsed, quality, details, videoId);

            var newEntry = new CachedVideoEntry(
                parsed,
                details,
                _timeProvider.GetUtcNow(),
                resolvedMedia?.ExpiresAt,
                new ConcurrentDictionary<string, ResolvedMedia>(StringComparer.OrdinalIgnoreCase));
            if (resolvedMedia is not null)
                newEntry.FormatsByQuality[quality] = resolvedMedia;

            _cache.Set(videoId, newEntry);
            return YtDlpFormatSelector.GetAvailableQualityTracks(parsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "Failed to load quality tracks");
            return [];
        }
        finally
        {
            ReleaseFetchLock(videoId, fetchLock, acquired);
        }
    }

    /// <summary>
    ///     Fallback entry for mpv ytdl_hook failures: accepts a bare video id or watch/shorts/share URL,
    ///     then delegates to <see cref="ResolveMediaAsync" /> (cache/timeout reuse). Never throws.
    /// </summary>
    public async Task<YouTubeMediaResolutionResult> TryResolveAsFallbackAsync(
        string videoIdOrUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var videoId = NormalizeToVideoId(videoIdOrUrl);
            if (videoId is null)
                return new YouTubeMediaResolutionResult.Failure("Media is unavailable for this video.");
            return await ResolveMediaAsync(videoId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Fallback media resolution failed");
            return new YouTubeMediaResolutionResult.Failure("Media is unavailable for this video.");
        }
    }

    private static string? NormalizeToVideoId(string? videoIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(videoIdOrUrl))
            return null;
        var text = videoIdOrUrl.Trim();
        if (PlaybackRequest.LooksLikeYouTubeVideoId(text))
            return text;
        return YouTubeUrlParser.Parse(text) switch
        {
            YouTubeUrlParseResult.Video(var videoId) => videoId,
            YouTubeUrlParseResult.Shorts(var videoId) => videoId,
            _ => null
        };
    }

    public void Invalidate(string videoId)
    {
        _cache.Remove(videoId);
    }

    private RefCountedLock AcquireFetchLock(string videoId)
    {
        lock (_fetchLocksGate)
        {
            if (!_fetchLocks.TryGetValue(videoId, out var entry))
            {
                entry = new RefCountedLock();
                _fetchLocks[videoId] = entry;
            }

            entry.RefCount++;
            return entry;
        }
    }

    private void ReleaseFetchLock(string videoId, RefCountedLock entry, bool acquired)
    {
        if (acquired)
            entry.Semaphore.Release();

        lock (_fetchLocksGate)
        {
            if (--entry.RefCount == 0 &&
                _fetchLocks.TryGetValue(videoId, out var current) &&
                ReferenceEquals(current, entry))
            {
                _fetchLocks.Remove(videoId);
                entry.Semaphore.Dispose();
            }
        }
    }

    private CachedVideoEntry? TryGetValidEntry(string videoId, bool forceRefresh, bool requireMedia = false)
    {
        if (forceRefresh)
        {
            _cache.Remove(videoId);
            return null;
        }

        if (!_cache.TryGetValue(videoId, out var entry)) return null;
        if (requireMedia && entry.Payload is null) return null;
        var now = _timeProvider.GetUtcNow();
        if (now - entry.CachedAt <= _cacheDuration && !IsMediaExpired(entry.MediaExpiresAt)) return entry;
        _cache.Remove(videoId);

        return null;
    }

    private ResolvedMedia? TrySelectMedia(
        ParsedYtDlpMedia payload,
        string quality,
        YouTubeVideoDetails? details,
        string videoId)
    {
        try
        {
            var preferredCodec = _preferencesService.GetPreferences().PreferredCodec;
            return YtDlpFormatSelector.SelectMedia(payload, quality, details, preferredCodec);
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
        return now >= expiresAt.Value - TimeSpan.FromSeconds(30);
    }

    private async Task<(bool IsSuccess, string? RawJsonOutput, string? ErrorMessage)> FetchFromYtDlpAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        Logger.Information("Extracting media formats for video");

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
            Logger.Warning(ex, "Timeout extracting media");
            return (false, null, RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to execute yt-dlp to extract media");
            return (false, null, RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            var stderr = DiagnosticSanitizer.Sanitize(processResult.StandardError.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Warning("yt-dlp extraction failed with exit code {ExitCode}. Stderr: {StdErr}",
                    processResult.ExitCode, stderr);

            var errorDetail = !string.IsNullOrWhiteSpace(stderr)
                ? $"the process exited with error code {processResult.ExitCode}: {stderr}"
                : $"the process exited with error code {processResult.ExitCode}.";

            return (false, null, RuntimeDependencyGuidance.YtDlpFailed(errorDetail));
        }

        if (!string.IsNullOrWhiteSpace(processResult.StandardOutput)) return (true, processResult.StandardOutput, null);
        if (!string.IsNullOrWhiteSpace(processResult.StandardError))
            Logger.Warning("yt-dlp extraction returned empty output. Stderr: {StdErr}",
                DiagnosticSanitizer.Sanitize(processResult.StandardError.Trim()));

        return (false, null, RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));
    }

    private async Task<(bool IsSuccess, YouTubeVideoDetails? Details, string? ErrorMessage)>
        FetchVideoDetailsFromApiAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            var (summary, description, _, _, _) = await _clientProvider.GetClient().Videos
                .GetAsync(VideoId.Parse(videoId), cancellationToken)
                .ConfigureAwait(false);
            return (true, new YouTubeVideoDetails(
                description,
                summary.Statistics.ViewCount,
                summary.PublishedAt,
                summary.Title,
                summary.Channel.Title), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "YoutubeAPI failed to load details");
            return (false, null,
                $"YoutubeAPI could not load video details: {DiagnosticSanitizer.Sanitize(exception.Message)}");
        }
    }

    private sealed class RefCountedLock
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount;
    }

    private sealed record CachedVideoEntry(
        ParsedYtDlpMedia? Payload,
        YouTubeVideoDetails? Details,
        DateTimeOffset CachedAt,
        DateTimeOffset? MediaExpiresAt,
        ConcurrentDictionary<string, ResolvedMedia> FormatsByQuality);
}
