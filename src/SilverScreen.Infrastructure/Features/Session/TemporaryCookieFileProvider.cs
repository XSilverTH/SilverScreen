using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using System.Text;

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

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Temporary cookie files require Linux.");
        }

        var directoryPath = Path.Combine(_tempRoot, $"silverscreen-cookies-{Guid.NewGuid():N}");
        var directoryCreated = false;
        try
        {
            Directory.CreateDirectory(directoryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            directoryCreated = true;

            var cookieFilePath = Path.Combine(directoryPath, "cookies.txt");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };
            using (var stream = new FileStream(cookieFilePath, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(cookies.Content);
            }

            return new CookieFileLease(cookieFilePath, directoryPath);
        }
        catch
        {
            if (directoryCreated && Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }

            throw;
        }
    }
}