using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Common;

/// <summary>
///     Provides XDG cache file path resolution, session-hash keys, and atomic JSON persistence for disk-backed caches.
/// </summary>
internal static class PersistentCacheFile
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PersistentCacheFile));

    public static string? TryGetActiveSessionHash(ISessionService sessionService)
    {
        var session = sessionService.GetCurrentSession();
        var cookies = sessionService.GetManualSessionCookies();
        if (session is not { IsSignedIn: true, HasManualSession: true } ||
            cookies is not { Format: SessionCookieFormat.NetscapeCookiesText } ||
            string.IsNullOrWhiteSpace(cookies.Content))
            return null;

        return HashSessionCookies(cookies.Content);
    }

    public static string HashSessionCookies(string cookieContent)
    {
        Span<byte> hashBytes = stackalloc byte[SHA256.HashSizeInBytes];
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(cookieContent.Length);
        if (maxByteCount <= 512)
        {
            Span<byte> buffer = stackalloc byte[512];
            var written = Encoding.UTF8.GetBytes(cookieContent, buffer);
            SHA256.HashData(buffer[..written], hashBytes);
            return Convert.ToHexString(hashBytes);
        }

        var rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(cookieContent, rented);
            SHA256.HashData(rented.AsSpan(0, written), hashBytes);
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented.AsSpan(0, maxByteCount));
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static string GetCacheFilePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cacheHome = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, ".cache");
        }

        return Path.Combine(cacheHome, "SilverScreen", fileName);
    }

    public static T? Read<T>(string filePath, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read persistent cache file {CacheFilePath}", filePath);
            return null;
        }
    }

    public static void WriteAtomically<T>(string filePath, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory ?? Directory.GetCurrentDirectory(),
                $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, value, typeInfo);
                    stream.Flush(true);
                }

                File.Move(temporaryPath, filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to write persistent cache file {CacheFilePath}", filePath);
        }
    }

    public static void Delete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete persistent cache file {CacheFilePath}", filePath);
        }
    }
}