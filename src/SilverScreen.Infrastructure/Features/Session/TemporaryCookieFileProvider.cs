using System.Text;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Session;

public sealed class TemporaryCookieFileProvider(ISessionService sessionService, string? tempRoot = null)
    : ICookieFileProvider
{
    private static readonly ILogger Logger = Log.ForContext<TemporaryCookieFileProvider>();
    private readonly string _tempRoot = tempRoot ?? Path.GetTempPath();

    public CookieFileLease? CreateCookieFile()
    {
        var cookies = sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content)) return null;

        if (cookies.Format != SessionCookieFormat.NetscapeCookiesText) return null;

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Temporary cookie files require Linux.");

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
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            };
            using (var stream = new FileStream(cookieFilePath, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(cookies.Content);
            }
            Logger.Debug("Created temporary cookie lease at {CookieFilePath}", cookieFilePath);
            return new CookieFileLease(cookieFilePath, directoryPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to create temporary cookie file in {TempRoot}", _tempRoot);
            if (directoryCreated && Directory.Exists(directoryPath)) Directory.Delete(directoryPath, true);

            throw;
        }
    }
}