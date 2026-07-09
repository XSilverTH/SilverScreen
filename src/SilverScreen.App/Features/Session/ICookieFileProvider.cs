namespace SilverScreen.Features.Session;

public interface ICookieFileProvider
{
    CookieFileLease? CreateCookieFile();
}

public sealed class CookieFileLease : IDisposable
{
    private readonly string? _directoryPath;
    private bool _disposed;

    public CookieFileLease(string path, string? directoryPath = null)
    {
        Path = path;
        _directoryPath = directoryPath;
    }

    public string Path { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TryDeleteFile(Path);

        if (_directoryPath is not null)
        {
            TryDeleteDirectory(_directoryPath);
        }
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
            Directory.Delete(path, recursive: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
