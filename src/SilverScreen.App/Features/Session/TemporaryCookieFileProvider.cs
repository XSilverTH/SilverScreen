using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Session;

public sealed class TemporaryCookieFileProvider : ICookieFileProvider
{
    private readonly ISessionService _sessionService;
    private readonly string _tempRoot;

    public TemporaryCookieFileProvider(ISessionService sessionService, string? tempRoot = null)
    {
        _sessionService = sessionService;
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    public CookieFileLease? CreateCookieFile()
    {
        var cookies = _sessionService.GetManualSessionCookies();
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
