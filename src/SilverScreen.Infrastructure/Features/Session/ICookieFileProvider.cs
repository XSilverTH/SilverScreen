namespace SilverScreen.Infrastructure.Features.Session;

public interface ICookieFileProvider
{
    CookieFileLease? CreateCookieFile();
}

public sealed class CookieFileLease(string path, string? directoryPath = null) : IDisposable
{
    private bool _disposed;

    public string Path { get; } = path;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        TryDeleteFile(Path);

        if (directoryPath is not null) TryDeleteDirectory(directoryPath);
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