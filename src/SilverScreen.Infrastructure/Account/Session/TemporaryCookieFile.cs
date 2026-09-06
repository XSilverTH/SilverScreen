using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

public static class TemporaryCookieFile
{
    internal const string DirectoryPrefix = "silverscreen-cookies-";
    internal const string IpcDirectoryPrefix = "silverscreen-mpv-";
    private static readonly TimeSpan DefaultStaleAge = TimeSpan.FromHours(24);

    private static readonly ILogger Logger = Log.ForContext(typeof(TemporaryCookieFile));

    /// <summary>
    /// Best-effort lease creation. Never throws: returns <c>null</c> when there are
    /// no cookies to persist or the temporary file could not be created, so playback
    /// proceeds without saved sign-in instead of surfacing an exception to the UI.
    /// </summary>
    public static CookieFileLease? CreateLease(string? cookieContent, string? tempRoot = null)
    {
        return TryCreateLease(cookieContent, tempRoot).Lease;
    }

    /// <summary>
    /// Best-effort lease creation with a user-facing error string. Never throws.
    /// The error is <c>null</c> when a lease was created or no cookies were needed;
    /// otherwise it explains the failure for status-line guidance.
    /// </summary>
    public static (CookieFileLease? Lease, string? ErrorMessage) TryCreateLease(
        string? cookieContent,
        string? tempRoot = null)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            return (null, null);

        var root = tempRoot ?? Path.GetTempPath();
        var directoryPath = Path.Combine(root, $"{DirectoryPrefix}{Guid.NewGuid():N}");
        try
        {
            if (OperatingSystem.IsLinux())
                Directory.CreateDirectory(directoryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else
                Directory.CreateDirectory(directoryPath);

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

            // Only the cookie file path (never its contents) reaches the logs.
            Logger.Debug("Created temporary cookie lease at {CookieFilePath}", cookieFilePath);
            return (new CookieFileLease(cookieFilePath, directoryPath), null);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to create temporary cookie file in {TempRoot}", root);
            DeleteDirectoryRecursively(directoryPath);
            return (null,
                "Could not prepare the temporary sign-in file, continuing without saved sign-in.");
        }
    }

    /// <summary>
    /// Deletes orphaned 0700 cookie/IPC directories and 0600 cookie files older than
    /// <paramref name="maxAge"/> matching our temp prefixes. Best-effort, never throws.
    /// </summary>
    public static void SweepStale(TimeSpan? maxAge = null, string? tempRoot = null)
    {
        var age = maxAge ?? DefaultStaleAge;
        var root = tempRoot ?? Path.GetTempPath();
        try
        {
            var rootDirectory = new DirectoryInfo(root);
            if (!rootDirectory.Exists)
                return;

            var now = DateTime.UtcNow;
            var removed = 0;

            foreach (var prefix in new[] { DirectoryPrefix, IpcDirectoryPrefix })
            {
                IEnumerable<DirectoryInfo> directories;
                try
                {
                    directories = rootDirectory.EnumerateDirectories($"{prefix}*");
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Stale sweep could not enumerate directories with prefix {Prefix}", prefix);
                    continue;
                }

                foreach (var directory in directories)
                {
                    try
                    {
                        directory.Refresh();
                        if (now - directory.LastWriteTimeUtc < age)
                            continue;

                        // Only our own empty-or-cookie directories are removed, recursively.
                        directory.Delete(true);
                        removed++;
                        Logger.Debug("Removed stale temporary directory {Directory}", directory.FullName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Stale sweep could not remove directory {Directory}", directory.FullName);
                    }
                }

                try
                {
                    foreach (var file in rootDirectory.EnumerateFiles($"{prefix}*"))
                    {
                        try
                        {
                            file.Refresh();
                            if (now - file.LastWriteTimeUtc < age)
                                continue;

                            file.Delete();
                            removed++;
                            Logger.Debug("Removed stale temporary file {File}", file.FullName);
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug(ex, "Stale sweep could not remove file {File}", file.FullName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Stale sweep could not enumerate files with prefix {Prefix}", prefix);
                }
            }

            if (removed > 0)
                Logger.Information("Removed {Count} stale temporary cookie/IPC entries from {TempRoot}", removed,
                    root);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Stale temporary file sweep failed in {TempRoot}", root);
        }
    }

    private static void DeleteDirectoryRecursively(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
                Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not remove partial temporary cookie directory {Directory}", directoryPath);
        }
    }
}
