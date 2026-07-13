using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

public sealed class TemporaryCookieFileProvider(ISessionService sessionService, string? tempRoot = null)
    : ICookieFileProvider
{
    private readonly string _tempRoot = tempRoot ?? Path.GetTempPath();

    public CookieFileLease? CreateCookieFile()
    {
        var cookies = sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            return null;
        }

        if (cookies.Format != SessionCookieFormat.NetscapeCookiesText)
        {
            return null;
        }

        var directoryPath = Path.Combine(_tempRoot, $"silverscreen-cookies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        TrySetDirectoryMode(directoryPath);

        var cookieFilePath = Path.Combine(directoryPath, "cookies.txt");
        File.WriteAllText(cookieFilePath, cookies.Content);
        TrySetFileMode(cookieFilePath);

        return new CookieFileLease(cookieFilePath, directoryPath);
    }

    private static void TrySetDirectoryMode(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private static void TrySetFileMode(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}