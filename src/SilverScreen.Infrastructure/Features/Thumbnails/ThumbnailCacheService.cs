using System.Net;
using System.Security.Cryptography;
using System.Text;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Thumbnails;

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

    public string CacheDirectory { get; }

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
            if (IsWebPFile(cachePath))
            {
                DeleteFileIfExists(cachePath);
            }
            else
            {
                TouchCacheFile(cachePath);
                return new ThumbnailResult(cachePath, true);
            }
        }

        Directory.CreateDirectory(CacheDirectory);
        var temporaryPath = Path.Combine(CacheDirectory, $"{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");
        var downloadUri = GetYouTubeJpegFallbackUri(uri) ?? uri;

        var downloadCompleted = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            request.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,*/*;q=0.1");
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
            return null;
        }
        finally
        {
            if (!downloadCompleted)
                DeleteFileIfExists(temporaryPath);
        }

        if (IsWebPFile(temporaryPath))
        {
            DeleteFileIfExists(temporaryPath);
            return null;
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

    public string GetCachePathForUrl(string thumbnailUrl)
    {
        return TryCreateHttpUri(thumbnailUrl, out var uri) ? GetCachePath(uri) : string.Empty;
    }

    public static string CreateCacheKey(string thumbnailUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(thumbnailUrl));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

    private static Uri? GetYouTubeJpegFallbackUri(Uri uri)
    {
        if (!IsYouTubeThumbnailHost(uri.Host))
            return null;

        var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length < 2 || pathSegments[0] is not ("vi" or "vi_webp"))
            return null;

        var videoId = Uri.UnescapeDataString(pathSegments[1]);
        return string.IsNullOrWhiteSpace(videoId)
            ? null
            : new Uri($"https://i.ytimg.com/vi/{Uri.EscapeDataString(videoId)}/maxresdefault.jpg");
    }

    private static bool IsYouTubeThumbnailHost(string host)
    {
        return host.Equals("i.ytimg.com", StringComparison.OrdinalIgnoreCase)
               || host.Equals("img.youtube.com", StringComparison.OrdinalIgnoreCase);
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

    private static bool IsWebPFile(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using var file = File.OpenRead(path);
            if (file.Length < header.Length || file.Read(header) != header.Length)
                return false;

            return header[0] == (byte)'R'
                   && header[1] == (byte)'I'
                   && header[2] == (byte)'F'
                   && header[3] == (byte)'F'
                   && header[8] == (byte)'W'
                   && header[9] == (byte)'E'
                   && header[10] == (byte)'B'
                   && header[11] == (byte)'P';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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