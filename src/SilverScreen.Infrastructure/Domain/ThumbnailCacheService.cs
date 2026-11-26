using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using System.Buffers;

namespace SilverScreen.Infrastructure.Domain;

public sealed class ThumbnailCacheService : IThumbnailService, IDisposable
{
    private const long DefaultMaxDownloadBytes = 3 * 1024 * 1024;
    private const int DefaultMaxFileCount = 300;
    private static readonly TimeSpan TouchDebounceInterval = TimeSpan.FromHours(1);
    private static readonly ILogger Logger = Log.ForContext<ThumbnailCacheService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> SafeExtensions =
    [
        with(StringComparer.OrdinalIgnoreCase),
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp"
    ];

    private readonly bool _disposeHttpClient;
    private readonly CancellationTokenSource _disposeCts = new();

    // Bounded LRU index: every add/hit path evicts oldest entries past _maxFileCount,
    // so in-memory growth stays flat no matter how many distinct thumbnails are requested.
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entryLookup =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<string, Lazy<Task<ThumbnailResult?>>> _inFlightDownloads =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly Lock _initLock = new();
    private readonly Lock _lock = new();
    private readonly LinkedList<CacheEntry> _lruEntries = new();
    private readonly long _maxDownloadBytes;
    private readonly int _maxFileCount;
    private volatile bool _initialized;

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
            EnsureInitialized();
            lock (_lock)
            {
                return _entryLookup.Count;
            }
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
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
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCreateHttpUri(thumbnailUrl, out var uri))
            return null;

        EnsureInitialized();

        var cachePath = GetCachePath(uri);
        if (File.Exists(cachePath))
        {
            RecordHitAndMaybeTouch(cachePath);
            Logger.Debug("Thumbnail cache hit");
            return new ThumbnailResult(cachePath, true);
        }

        return await RunSingleflightDownloadAsync(uri, cachePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ThumbnailResult?> RunSingleflightDownloadAsync(
        Uri uri,
        string cachePath,
        CancellationToken cancellationToken)
    {
        Lazy<Task<ThumbnailResult?>>? created = null;
        var lazy = _inFlightDownloads.GetOrAdd(cachePath, _ =>
        {
            created = new Lazy<Task<ThumbnailResult?>>(
                () => ExecuteSingleflightAsync(uri, cachePath, created!),
                LazyThreadSafetyMode.ExecutionAndPublication);
            return created;
        });

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ThumbnailResult?> ExecuteSingleflightAsync(
        Uri uri,
        string cachePath,
        Lazy<Task<ThumbnailResult?>> self)
    {
        try
        {
            return await DownloadAndCacheAsync(uri, cachePath, _disposeCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _inFlightDownloads.TryRemove(new KeyValuePair<string, Lazy<Task<ThumbnailResult?>>>(cachePath, self));
        }
    }

    private async Task<ThumbnailResult?> DownloadAndCacheAsync(
        Uri uri,
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(cachePath))
        {
            RecordHitAndMaybeTouch(cachePath);
            Logger.Debug("Thumbnail cache hit");
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
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            Logger.Warning(ex, "Failed to download thumbnail");
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
                RecordHitAndMaybeTouch(cachePath);
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
                RecordHitAndMaybeTouch(cachePath);
                return new ThumbnailResult(cachePath, true);
            }

            Logger.Warning(ex, "Failed to cache thumbnail");
            return null;
        }
    }

    private static string CreateCacheKey(string thumbnailUrl)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        var maxBytes = Encoding.UTF8.GetMaxByteCount(thumbnailUrl.Length);
        if (maxBytes <= 512)
        {
            Span<byte> buffer = stackalloc byte[512];
            var written = Encoding.UTF8.GetBytes(thumbnailUrl, buffer);
            SHA256.HashData(buffer[..written], hash);
            return Convert.ToHexStringLower(hash);
        }

        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var written = Encoding.UTF8.GetBytes(thumbnailUrl, rented);
            SHA256.HashData(rented.AsSpan(0, written), hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
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
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 16,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            ResponseDrainTimeout = TimeSpan.FromSeconds(2),
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler, true)
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
        if (!string.IsNullOrWhiteSpace(thumbnailUrl) && thumbnailUrl.StartsWith("//", StringComparison.Ordinal))
            thumbnailUrl = $"https:{thumbnailUrl}";

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
        // Rented per download instead of allocating; always returned below.
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        Exception? initException = null;
        List<string>? filesToDelete = null;

        lock (_initLock)
        {
            if (_initialized)
                return;

            List<CacheEntry>? entriesToAdd = null;
            if (Directory.Exists(CacheDirectory))
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var tmpCutoff = now.AddMinutes(-5);
                    var entries = new List<CacheEntry>();
                    var dirInfo = new DirectoryInfo(CacheDirectory);
                    foreach (var fileInfo in dirInfo.EnumerateFiles())
                    {
                        var filePath = fileInfo.FullName;
                        if (fileInfo.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                if (fileInfo.LastWriteTimeUtc < tmpCutoff)
                                {
                                    filesToDelete ??= [];
                                    filesToDelete.Add(filePath);
                                }
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                            }

                            continue;
                        }

                        try
                        {
                            entries.Add(new CacheEntry(filePath, fileInfo.LastWriteTimeUtc));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                        }
                    }

                    entries.Sort((a, b) => a.LastTouchedUtc.CompareTo(b.LastTouchedUtc));

                    var excessCount = entries.Count - _maxFileCount;
                    if (excessCount > 0)
                    {
                        filesToDelete ??= [];
                        for (var i = 0; i < excessCount; i++)
                            filesToDelete.Add(entries[i].Path);
                        entriesToAdd = entries.GetRange(excessCount, entries.Count - excessCount);
                    }
                    else
                    {
                        entriesToAdd = entries;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    initException = ex;
                }
            }

            if (entriesToAdd is { Count: > 0 })
                lock (_lock)
                {
                    foreach (var entry in entriesToAdd)
                    {
                        if (_entryLookup.ContainsKey(entry.Path)) continue;
                        var node = _lruEntries.AddLast(entry);
                        _entryLookup[entry.Path] = node;
                    }
                }

            _initialized = true;
        }

        if (initException is not null)
            Logger.Warning(initException, "Failed to initialize thumbnail cache index from disk");

        if (filesToDelete is null) return;
        foreach (var file in filesToDelete)
            DeleteFileIfExists(file);
    }

    private void RecordHitAndMaybeTouch(string cachePath)
    {
        EnsureInitialized();
        List<string>? filesToDelete = null;
        var shouldTouchDisk = false;
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            if (_entryLookup.TryGetValue(cachePath, out var node))
            {
                if (now - node.Value.LastTouchedUtc >= TouchDebounceInterval)
                {
                    node.Value.LastTouchedUtc = now;
                    shouldTouchDisk = true;
                }

                if (node != _lruEntries.Last)
                {
                    _lruEntries.Remove(node);
                    _lruEntries.AddLast(node);
                }
            }
            else
            {
                var newNode = _lruEntries.AddLast(new CacheEntry(cachePath, now));
                _entryLookup[cachePath] = newNode;
                shouldTouchDisk = true;

                while (_lruEntries.Count > _maxFileCount && _lruEntries.First is { } oldest)
                {
                    _lruEntries.RemoveFirst();
                    _entryLookup.Remove(oldest.Value.Path);
                    filesToDelete ??= [];
                    filesToDelete.Add(oldest.Value.Path);
                }
            }
        }

        if (shouldTouchDisk)
            Task.Run(() => TouchCacheFile(cachePath)).FireAndForget(Logger);

        if (filesToDelete is null) return;
        foreach (var file in filesToDelete)
            DeleteFileIfExists(file);
    }

    private void RecordAddAndEvict(string cachePath)
    {
        EnsureInitialized();
        List<string>? filesToDelete = null;
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            if (_entryLookup.TryGetValue(cachePath, out var node))
            {
                node.Value.LastTouchedUtc = now;
                if (node != _lruEntries.Last)
                {
                    _lruEntries.Remove(node);
                    _lruEntries.AddLast(node);
                }
            }
            else
            {
                var newNode = _lruEntries.AddLast(new CacheEntry(cachePath, now));
                _entryLookup[cachePath] = newNode;
            }

            while (_lruEntries.Count > _maxFileCount && _lruEntries.First is { } oldest)
            {
                _lruEntries.RemoveFirst();
                _entryLookup.Remove(oldest.Value.Path);
                filesToDelete ??= [];
                filesToDelete.Add(oldest.Value.Path);
            }
        }

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

    private sealed class CacheEntry(string path, DateTime lastTouchedUtc)
    {
        public string Path { get; } = path;
        public DateTime LastTouchedUtc { get; set; } = lastTouchedUtc;
    }
}
