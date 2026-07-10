using System.Net;
using System.Security.Cryptography;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Thumbnails;

public sealed class ThumbnailCacheService : IThumbnailService, IDisposable
{
    public const long DefaultMaxDownloadBytes = 3 * 1024 * 1024;
    public const int DefaultMaxFileCount = 300;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly string _cacheDirectory;
    private readonly long _maxDownloadBytes;
    private readonly int _maxFileCount;

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
        {
            throw new ArgumentOutOfRangeException(nameof(maxDownloadBytes), "Maximum thumbnail download size must be positive.");
        }

        if (maxFileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileCount), "Maximum thumbnail cache count must be positive.");
        }

        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _maxDownloadBytes = maxDownloadBytes;
        _maxFileCount = maxFileCount;
        _disposeHttpClient = disposeHttpClient;
    }

    public string CacheDirectory => _cacheDirectory;

    public async Task<ThumbnailResult?> GetThumbnailAsync(VideoSummary video, CancellationToken cancellationToken = default)
    {
        if (video.IsShort)
        {
            return null;
        }

        return await GetThumbnailAsync(video.ThumbnailUrl, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(string thumbnailUrl, CancellationToken cancellationToken = default)
    {
        if (!TryCreateHttpUri(thumbnailUrl, out var uri))
        {
            return null;
        }

        var cachePath = GetCachePath(uri);
        if (File.Exists(cachePath))
        {
            TouchCacheFile(cachePath);
            return new ThumbnailResult(cachePath, WasCacheHit: true);
        }

        Directory.CreateDirectory(_cacheDirectory);
        var temporaryPath = Path.Combine(_cacheDirectory, $"{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");

        var downloadCompleted = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is { } contentLength && contentLength > _maxDownloadBytes)
            {
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(temporaryPath);
            var copied = await CopyWithLimitAsync(source, target, _maxDownloadBytes, cancellationToken).ConfigureAwait(false);
            if (!copied)
            {
                return null;
            }

            downloadCompleted = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (!downloadCompleted)
            {
                DeleteFileIfExists(temporaryPath);
            }
        }

        try
        {
            if (File.Exists(cachePath))
            {
                File.Delete(temporaryPath);
                TouchCacheFile(cachePath);
                return new ThumbnailResult(cachePath, WasCacheHit: true);
            }

            File.Move(temporaryPath, cachePath);
            CleanupOldCacheFiles();
            return new ThumbnailResult(cachePath, WasCacheHit: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteFileIfExists(temporaryPath);
            return null;
        }
    }

    public string GetCachePathForUrl(string thumbnailUrl)
    {
        return TryCreateHttpUri(thumbnailUrl, out var uri) ? GetCachePath(uri) : string.Empty;
    }

    public static string CreateCacheKey(string thumbnailUrl)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(thumbnailUrl));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static string GetDefaultCacheDirectory()
    {
        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cacheHome = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, ".cache");
        }

        return Path.Combine(cacheHome, "SilverScreen", "thumbnails");
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient
        {
            Timeout = DefaultTimeout,
        };
    }

    private string GetCachePath(Uri uri)
    {
        return Path.Combine(_cacheDirectory, $"{CreateCacheKey(uri.AbsoluteUri)}{GetSafeExtension(uri)}");
    }

    private static bool TryCreateHttpUri(string thumbnailUrl, out Uri uri)
    {
        if (Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static string GetSafeExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return SafeExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".img";
    }

    private static async Task<bool> CopyWithLimitAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return true;
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
    }

    private void CleanupOldCacheFiles()
    {
        try
        {
            var files = Directory.EnumerateFiles(_cacheDirectory)
                .Where(file => !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(_maxFileCount)
                .ToList();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
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
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
