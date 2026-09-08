using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

/// <summary>
///     Creates and reaps 0600 cookie files inside 0700 per-lease temp directories.
///     Lease-holder lifetimes are intentionally minimal; nothing holds a lease longer
///     than its consumer needs it:
///     <list type="bullet">
///         <item>
///             yt-dlp resolve (<c>YtDlpMediaResolver</c>): <c>using</c>-scoped to a single
///             resolve call; disposed when the call returns. Shortest lifetime.
///         </item>
///         <item>
///             External mpv (<c>ExternalMpvPlaybackService</c>): held only until the mpv
///             process exits (or start fails), then disposed; never longer than the process.
///         </item>
///         <item>
///             Embedded playback (<c>PlaybackSession</c>): held for the session lifetime
///             and released in teardown/dispose.
///         </item>
///     </list>
///     Disposal overwrites file bytes with zeros before deleting (best-effort; see
///     <c>TryWipeAndDeleteFile</c>). Only cookie file paths — never their contents —
///     are written to the logs.
/// </summary>
public static class TemporaryCookieFile
{
    internal const string DirectoryPrefix = "silverscreen-cookies-";
    private const string IpcDirectoryPrefix = "silverscreen-mpv-";
    private static readonly TimeSpan DefaultStaleAge = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, byte> ActiveLeaseDirectories = new();

    private static readonly ILogger Logger = Log.ForContext(typeof(TemporaryCookieFile));

    static TemporaryCookieFile()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WipeActiveLeases();

        if (!OperatingSystem.IsLinux()) return;

        try
        {
            PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => WipeActiveLeases());
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => WipeActiveLeases());
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not register POSIX signal handlers for cookie cleanup");
        }
    }

    public static string GetDefaultTempRoot()
    {
        if (!OperatingSystem.IsLinux()) return Path.GetTempPath();
        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdg) && Directory.Exists(xdg))
            return xdg;

        return Path.GetTempPath();
    }

    private static void WipeActiveLeases()
    {
        try
        {
            foreach (var directoryPath in ActiveLeaseDirectories.Keys)
            {
                DeleteDirectoryRecursively(directoryPath);
                ActiveLeaseDirectories.TryRemove(directoryPath, out _);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to clean up active cookie leases during exit");
        }
    }

    /// <summary>
    ///     Best-effort lease creation. Never throws: returns <c>null</c> when there are
    ///     no cookies to persist or the temporary file could not be created, so playback
    ///     proceeds without saved sign-in instead of surfacing an exception to the UI.
    /// </summary>
    public static CookieFileLease? CreateLease(string? cookieContent, string? tempRoot = null)
    {
        return TryCreateLease(cookieContent, tempRoot).Lease;
    }

    /// <summary>
    ///     Best-effort lease creation with a user-facing error string. Never throws.
    ///     The error is <c>null</c> when a lease was created or no cookies were needed;
    ///     otherwise it explains the failure for status-line guidance.
    /// </summary>
    private static (CookieFileLease? Lease, string? ErrorMessage) TryCreateLease(
        string? cookieContent,
        string? tempRoot = null)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            return (null, null);

        var root = tempRoot ?? GetDefaultTempRoot();
        var directoryPath = Path.Combine(root, $"{DirectoryPrefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
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

            ActiveLeaseDirectories.TryAdd(directoryPath, 0);
            // Only the cookie file path (never its contents) reaches the logs.
            Logger.Debug("Created temporary cookie lease at {CookieFilePath}", cookieFilePath);
            return (
                new CookieFileLease(cookieFilePath, directoryPath,
                    () => { ActiveLeaseDirectories.TryRemove(directoryPath, out _); }), null);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to create temporary cookie file in {TempRoot}", root);
            ActiveLeaseDirectories.TryRemove(directoryPath, out _);
            DeleteDirectoryRecursively(directoryPath);
            return (null,
                "Could not prepare the temporary sign-in file, continuing without saved sign-in.");
        }
    }

    /// <summary>
    ///     Deletes orphaned 0700 cookie/IPC directories and 0600 cookie files matching our temp prefixes.
    ///     Orphaned directories from dead sessions or untracked leases are purged immediately;
    ///     otherwise entries older than <paramref name="maxAge" /> (default 1 hour) are deleted.
    ///     Best-effort, never throws.
    /// </summary>
    public static void SweepStale(TimeSpan? maxAge = null, string? tempRoot = null)
    {
        var age = maxAge ?? DefaultStaleAge;
        if (tempRoot is not null)
        {
            SweepDirectory(tempRoot, age);
            return;
        }

        var defaultRoot = GetDefaultTempRoot();
        SweepDirectory(defaultRoot, age);

        var fallbackRoot = Path.GetTempPath();
        if (!string.Equals(defaultRoot, fallbackRoot, StringComparison.Ordinal)) SweepDirectory(fallbackRoot, age);
    }

    private static void SweepDirectory(string root, TimeSpan age)
    {
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
                    try
                    {
                        if (!IsDirectoryOrphanedOrStale(directory, prefix, age, now))
                            continue;
                        // Only our own empty-or-cookie directories are removed. Cookie bytes
                        // are overwritten before the recursive remove (sockets under the IPC
                        // prefix fail the overwrite and fall through to plain delete).
                        foreach (var staleFile in directory.EnumerateFiles("*", SearchOption.AllDirectories).ToList())
                            TryWipeAndDeleteFile(staleFile.FullName);
                        directory.Delete(true);
                        removed++;
                        Logger.Debug("Removed stale temporary directory {Directory}", directory.FullName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Stale sweep could not remove directory {Directory}", directory.FullName);
                    }

                try
                {
                    foreach (var file in rootDirectory.EnumerateFiles($"{prefix}*"))
                        try
                        {
                            if (!IsFileOrphanedOrStale(file, prefix, age, now))
                                continue;
                            TryWipeAndDeleteFile(file.FullName);
                            removed++;
                            Logger.Debug("Removed stale temporary file {File}", file.FullName);
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug(ex, "Stale sweep could not remove file {File}", file.FullName);
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

    private static bool IsDirectoryOrphanedOrStale(DirectoryInfo directory, string prefix, TimeSpan maxAge,
        DateTime now)
    {
        if (!TryExtractPid(directory.Name, prefix, out var pid)) return now - directory.LastWriteTimeUtc >= maxAge;
        if (pid == Environment.ProcessId)
            return !IsActiveLease(directory.FullName);

        if (!IsProcessAlive(pid))
            return true;

        return now - directory.LastWriteTimeUtc >= maxAge;
    }

    private static bool IsFileOrphanedOrStale(FileInfo file, string prefix, TimeSpan maxAge, DateTime now)
    {
        if (!TryExtractPid(file.Name, prefix, out var pid)) return now - file.LastWriteTimeUtc >= maxAge;
        if (pid == Environment.ProcessId)
            return !IsActiveLeaseFile(file.FullName);

        if (!IsProcessAlive(pid))
            return true;

        return now - file.LastWriteTimeUtc >= maxAge;
    }

    private static bool TryExtractPid(string name, string prefix, out int pid)
    {
        pid = 0;
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var suffix = name[prefix.Length..];
        var dashIndex = suffix.IndexOf('-');
        if (dashIndex <= 0)
            return false;

        return int.TryParse(suffix.AsSpan(0, dashIndex), out pid) && pid > 0;
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not query process {Pid} state; assuming alive as safeguard", pid);
            return true;
        }
    }

    private static bool IsActiveLease(string directoryFullName)
    {
        try
        {
            var normalized = Path.GetFullPath(directoryFullName);
            if (ActiveLeaseDirectories.Keys.Any(key => string.Equals(Path.GetFullPath(key), normalized,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                return true;
        }
        catch
        {
            // Ignore path normalization errors
        }

        return false;
    }

    private static bool IsActiveLeaseFile(string fileFullName)
    {
        try
        {
            var directory = Path.GetDirectoryName(fileFullName);
            if (directory is not null)
                return IsActiveLease(directory);
        }
        catch
        {
            // Ignore path normalization errors
        }

        return false;
    }

    private static void DeleteDirectoryRecursively(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            // A half-written lease directory may already hold cookie bytes: wipe first.
            foreach (var partialFile in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                         .ToList())
                TryWipeAndDeleteFile(partialFile);
            Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not remove partial temporary cookie directory {Directory}", directoryPath);
        }
    }

    /// <summary>
    ///     Best-effort overwrite-before-delete: fills the file with zeros across its full
    ///     length, flushes to storage, then deletes it. Never throws. This is data hygiene,
    ///     not a guarantee against forensic recovery (copy-on-write filesystems, journals,
    ///     and SSD wear-levelling may retain copies). Logs only the file path.
    /// </summary>
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
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not overwrite temporary file {CookieFilePath} before delete", path);
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not delete temporary file {CookieFilePath}", path);
        }
    }
}