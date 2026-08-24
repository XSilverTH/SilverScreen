using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Infrastructure.Browsing.Common;

public sealed class ThumbnailCacheService : IThumbnailService, IDisposable
{
    private const long DefaultMaxDownloadBytes = 3 * 1024 * 1024;
    private const int DefaultMaxFileCount = 300;
    private static readonly ILogger Logger = Log.ForContext<ThumbnailCacheService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp"
    };

    private readonly bool _disposeHttpClient;

    private readonly Dictionary<string, LinkedListNode<string>> _entryLookup =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly HttpClient _httpClient;

    private readonly Lock _lock = new();
    private readonly LinkedList<string> _lruEntries = new();
    private readonly long _maxDownloadBytes;
    private readonly int _maxFileCount;
    private bool _initialized;

    public ThumbnailCacheService()
        : this(CreateDefaultHttpClient(), GetDefaultCacheDirectory(), disposeHttpClient: true)
    {
    }

    public ThumbnailCacheService(
        HttpClient httpClient,
        string cacheDirectory,
        long maxDownloadBytes = DefaultMaxDownloadBytes,
        int maxFileCount = DefaultMaxFileCount,
        bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        if (maxDownloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDownloadBytes),
                "Maximum thumbnail download size must be positive.");

        if (maxFileCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileCount),
                "Maximum thumbnail cache count must be positive.");

        _httpClient = httpClient;
        CacheDirectory = cacheDirectory;
        _maxDownloadBytes = maxDownloadBytes;
        _maxFileCount = maxFileCount;
        _disposeHttpClient = disposeHttpClient;
    }

    private string CacheDirectory { get; }

    internal int CachedFileCount
    {
        get
        {
            lock (_lock)
            {
                EnsureInitializedLocked();
                return _entryLookup.Count;
            }
        }
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(VideoSummary video,
        CancellationToken cancellationToken = default)
    {
        if (video.IsShort)
            return null;

        return await GetThumbnailAsync(video.ThumbnailUrl, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(string thumbnailUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateHttpUri(thumbnailUrl, out var uri))
            return null;

        Exception? initException;
        lock (_lock)
        {
            initException = !_initialized ? EnsureInitializedLocked() : null;
        }

        if (initException is not null)
            Logger.Warning(initException, "Failed to initialize thumbnail cache index from disk.");

        var cachePath = GetCachePath(uri);
        if (File.Exists(cachePath))
        {
            RecordHit(cachePath);
            TouchCacheFile(cachePath);
            Logger.Debug("Thumbnail cache hit for {Url}", thumbnailUrl);
            return new ThumbnailResult(cachePath, true);
        }

        Directory.CreateDirectory(CacheDirectory);
        var temporaryPath = Path.Combine(CacheDirectory, $"{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");

        var downloadCompleted = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Accept", "image/webp,image/png,image/jpeg,*/*;q=0.8");
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
                return null;

            if (response.Content.Headers.ContentLength is { } contentLength && contentLength > _maxDownloadBytes)
                return null;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(temporaryPath);
            var copied = await CopyWithLimitAsync(source, target, _maxDownloadBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!copied)
                return null;

            downloadCompleted = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            Logger.Warning(ex, "Failed to download thumbnail from {DownloadUri}", uri);
            return null;
        }
        finally
        {
            if (!downloadCompleted)
                DeleteFileIfExists(temporaryPath);
        }

        try
        {
            if (File.Exists(cachePath))
            {
                DeleteFileIfExists(temporaryPath);
                RecordHit(cachePath);
                TouchCacheFile(cachePath);
                return new ThumbnailResult(cachePath, true);
            }

            File.Move(temporaryPath, cachePath);
            RecordAddAndEvict(cachePath);
            return new ThumbnailResult(cachePath, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteFileIfExists(temporaryPath);
            if (File.Exists(cachePath))
            {
                RecordHit(cachePath);
                TouchCacheFile(cachePath);
                return new ThumbnailResult(cachePath, true);
            }

            Logger.Warning(ex, "Failed to cache thumbnail for {CachePath}", cachePath);
            return null;
        }
    }

    private static string CreateCacheKey(string thumbnailUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(thumbnailUrl));
        return Convert.ToHexStringLower(bytes);
    }

    private static string GetDefaultCacheDirectory()
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(cacheHome)) return Path.Combine(cacheHome, "SilverScreen", "thumbnails");
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        cacheHome = string.IsNullOrWhiteSpace(userHome)
            ? Path.GetTempPath()
            : Path.Combine(userHome, ".cache");

        return Path.Combine(cacheHome, "SilverScreen", "thumbnails");
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    private string GetCachePath(Uri uri)
    {
        return Path.Combine(CacheDirectory, $"{CreateCacheKey(uri.AbsoluteUri)}{GetSafeExtension(uri)}");
    }

    private static bool TryCreateHttpUri(string thumbnailUrl, out Uri uri)
    {
        if (Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return true;

        uri = null!;
        return false;
    }

    private static string GetSafeExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return SafeExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".img";
    }


    private static async Task<bool> CopyWithLimitAsync(Stream source, Stream target, long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
                return true;

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
                return false;

            await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
    }

    private Exception? EnsureInitializedLocked()
    {
        if (_initialized)
            return null;

        _initialized = true;

        if (!Directory.Exists(CacheDirectory))
            return null;
        try
        {
            var entries = new List<(string Path, DateTime LastWriteTimeUtc)>();
            foreach (var file in Directory.EnumerateFiles(CacheDirectory))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-5))
                            DeleteFileIfExists(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }

                    continue;
                }

                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    entries.Add((file, lastWrite));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            entries.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

            var excessCount = entries.Count - _maxFileCount;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (i < excessCount)
                {
                    DeleteFileIfExists(entry.Path);
                }
                else
                {
                    if (_entryLookup.ContainsKey(entry.Path)) continue;
                    var node = _lruEntries.AddLast(entry.Path);
                    _entryLookup[entry.Path] = node;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return ex;
        }

        return null;
    }

    private void RecordHit(string cachePath)
    {
        List<string>? filesToDelete = null;
        Exception? initException;
        lock (_lock)
        {
            initException = EnsureInitializedLocked();
            if (_entryLookup.TryGetValue(cachePath, out var node))
            {
                if (node != _lruEntries.Last)
                {
                    _lruEntries.Remove(node);
                    _lruEntries.AddLast(node);
                }
            }
            else
            {
                var newNode = _lruEntries.AddLast(cachePath);
                _entryLookup[cachePath] = newNode;

                while (_lruEntries.Count > _maxFileCount && _lruEntries.First is { } oldest)
                {
                    _lruEntries.RemoveFirst();
                    _entryLookup.Remove(oldest.Value);
                    filesToDelete ??= [];
                    filesToDelete.Add(oldest.Value);
                }
            }
        }

        if (initException is not null)
            Logger.Warning(initException, "Failed to initialize thumbnail cache index from disk.");

        if (filesToDelete is null) return;
        foreach (var file in filesToDelete)
            DeleteFileIfExists(file);
    }

    private void RecordAddAndEvict(string cachePath)
    {
        List<string>? filesToDelete = null;
        Exception? initException;
        lock (_lock)
        {
            initException = EnsureInitializedLocked();
            if (_entryLookup.TryGetValue(cachePath, out var node))
            {
                if (node != _lruEntries.Last)
                {
                    _lruEntries.Remove(node);
                    _lruEntries.AddLast(node);
                }
            }
            else
            {
                var newNode = _lruEntries.AddLast(cachePath);
                _entryLookup[cachePath] = newNode;
            }

            while (_lruEntries.Count > _maxFileCount && _lruEntries.First is { } oldest)
            {
                _lruEntries.RemoveFirst();
                _entryLookup.Remove(oldest.Value);
                filesToDelete ??= [];
                filesToDelete.Add(oldest.Value);
            }
        }

        if (initException is not null)
            Logger.Warning(initException, "Failed to initialize thumbnail cache index from disk.");

        if (filesToDelete is null) return;
        foreach (var file in filesToDelete)
            DeleteFileIfExists(file);
    }

    private static void TouchCacheFile(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}