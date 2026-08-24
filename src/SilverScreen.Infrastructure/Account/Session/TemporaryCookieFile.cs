using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

public static class TemporaryCookieFile
{
    private static readonly ILogger Logger = Log.ForContext(typeof(TemporaryCookieFile));

    public static CookieFileLease? CreateLease(string? cookieContent, string? tempRoot = null)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            return null;

        var root = tempRoot ?? Path.GetTempPath();
        var directoryPath = Path.Combine(root, $"silverscreen-cookies-{Guid.NewGuid():N}");
        var directoryCreated = false;
        try
        {
            if (OperatingSystem.IsLinux())
                Directory.CreateDirectory(directoryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else
                Directory.CreateDirectory(directoryPath);
            directoryCreated = true;

            var cookieFilePath = Path.Combine(directoryPath, "cookies.txt");
            if (OperatingSystem.IsLinux())
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using var stream = new FileStream(cookieFilePath, options);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(cookieContent);
            }
            else
            {
                using var stream = new FileStream(cookieFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(cookieContent);
            }

            Logger.Debug("Created temporary cookie lease at {CookieFilePath}", cookieFilePath);
            return new CookieFileLease(cookieFilePath, directoryPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to create temporary cookie file in {TempRoot}", root);
            if (!directoryCreated || !Directory.Exists(directoryPath)) throw;
            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch
            {
                // ignored
            }

            throw;
        }
    }
}