using System.Net;
using System.Security.Cryptography;
using System.Text;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Thumbnails;

public sealed class ThumbnailCacheService : IThumbnailService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ThumbnailCacheService>();
    private const long DefaultMaxDownloadBytes = 3 * 1024 * 1024;
    private const int DefaultMaxFileCount = 300;
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

    private readonly HttpClient _httpClient;
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

        var cachePath = GetCachePath(uri);
        if (File.Exists(cachePath))
        {
            TouchCacheFile(cachePath);
            Logger.Debug("Thumbnail cache hit for {Url}", thumbnailUrl);
            return new ThumbnailResult(cachePath, true);
        }

        Directory.CreateDirectory(CacheDirectory);
        var temporaryPath = Path.Combine(CacheDirectory, $"{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
        var downloadUri = uri;

        var downloadCompleted = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
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
            Logger.Warning(ex, "Failed to download thumbnail from {DownloadUri}", downloadUri);
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
                File.Delete(temporaryPath);
                TouchCacheFile(cachePath);
                return new ThumbnailResult(cachePath, true);
            }

            File.Move(temporaryPath, cachePath);
            CleanupOldCacheFiles();
            return new ThumbnailResult(cachePath, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteFileIfExists(temporaryPath);
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

    private void CleanupOldCacheFiles()
    {
        try
        {
            var files = Directory.EnumerateFiles(CacheDirectory)
                .Where(file => !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(_maxFileCount)
                .ToList();

            foreach (var file in files)
                file.Delete();
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
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}