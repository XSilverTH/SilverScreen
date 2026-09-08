namespace SilverScreen.Core.Account.Session;

public interface ICookieFileProvider
{
    CookieFileLease? CreateCookieFile();
}

/// <summary>
///     Best-effort handle to a temporary cookie file. Disposal overwrites the file bytes
///     with zeros before deleting (best-effort, never throws), then removes the directory.
/// </summary>
public sealed class CookieFileLease(string path, string? directoryPath = null, Action? onDisposed = null) : IDisposable
{
    private bool _disposed;

    public string Path { get; } = path;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        try
        {
            TryWipeAndDeleteFile(Path);

            if (directoryPath is not null) TryDeleteDirectory(directoryPath);
        }
        finally
        {
            onDisposed?.Invoke();
        }
    }

    private static void TryWipeAndDeleteFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info is { Exists: true, Length: > 0 })
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                var zeros = new byte[4096];
                var remaining = info.Length;
                while (remaining > 0)
                {
                    var chunk = (int)Math.Min(zeros.Length, remaining);
                    stream.Write(zeros, 0, chunk);
                    remaining -= chunk;
                }

                stream.Flush(true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        TryDeleteFile(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}