using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
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
public static partial class TemporaryCookieFile
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

    ///     Deletes orphaned 0700 cookie/IPC directories created by this application. Cookie
    ///     leases must match the exact process-id/GUID directory pattern and contain only the
    ///     expected regular, non-symlink cookies.txt file. Unexpected entries are never
    ///     traversed or deleted. Orphaned directories from dead sessions or untracked leases
    ///     are purged immediately; otherwise entries older than <paramref name="maxAge" />
    ///     (default 1 hour) are deleted. Best-effort, never throws.
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
            if (!IsSafeDirectory(rootDirectory))
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
                        // Cookie directories have a deliberately strict name shape. A prefix
                        // alone is not proof that the application created the entry.
                        if (prefix == DirectoryPrefix && !TryExtractCookieLeasePid(directory.Name, out _))
                            continue;
                        if (!IsSafeDirectory(directory) ||
                            !IsDirectoryOrphanedOrStale(directory, prefix, age, now))
                            continue;

                        if (TryRemoveOwnedDirectory(directory, prefix))
                        {
                            removed++;
                            Logger.Debug("Removed stale temporary directory {Directory}", directory.FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Stale sweep could not remove directory {Directory}", directory.FullName);
                    }
            }

            // The application never creates files directly under the temp root. In particular,
            // do not treat a root-level prefix match as ownership: it could be an arbitrary file
            // or a link planted by another process.
            if (removed > 0)
                Logger.Information("Removed {Count} stale temporary cookie/IPC entries from {TempRoot}", removed,
                    root);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Stale temporary file sweep failed in {TempRoot}", root);
        }
    }

    private static bool TryRemoveOwnedDirectory(DirectoryInfo directory, string prefix)
    {
        if (!IsSafeDirectory(directory))
            return false;

        // Never recurse from a directory selected by a filename prefix. A nested directory,
        // link, or unexpected file is untrusted and must remain untouched.
        foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).ToList())
        {
            if (prefix == DirectoryPrefix)
            {
                if (entry is FileInfo file && IsOwnedCookieFile(file))
                    TryWipeAndDeleteFile(file.FullName);
            }
            else if (entry is FileInfo ipcFile && ipcFile.Name == "mpv.sock" && IsOwnedIpcSocket(ipcFile))
            {
                // mpv's endpoint is not a cookie and is not wiped, but it is still constrained
                // to the exact application-owned entry name.
                TryDeleteFile(ipcFile.FullName);
            }
        }

        try
        {
            // A non-recursive delete is essential: if an attacker races in an entry, the
            // directory remains and Directory.Delete can never remove that entry for us.
            directory.Delete(false);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Stale sweep left non-empty temporary directory {Directory}", directory.FullName);
            return false;
        }
    }

    private static bool IsSafeDirectory(DirectoryInfo directory)
    {
        try
        {
            return directory.Exists &&
                   (directory.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) ==
                   FileAttributes.Directory;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Stale sweep could not inspect directory {Directory}", directory.FullName);
            return false;
        }
    }

    private static bool IsRegularNonSymlink(FileInfo file)
    {
        try
        {
            if (!file.Exists ||
                (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return false;

            if (!OperatingSystem.IsLinux())
                return true;

            return LStat(file.FullName, out var stat) == 0 &&
                   (stat.Mode & UnixFileTypeMask) == UnixRegularFile;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Stale sweep could not inspect file {File}", file.FullName);
            return false;
        }
    }

    private static bool IsOwnedIpcSocket(FileInfo file)
    {
        try
        {
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                !OperatingSystem.IsLinux())
                return false;

            return LStat(file.FullName, out var stat) == 0 &&
                   (stat.Mode & UnixFileTypeMask) == UnixSocket;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Stale sweep could not inspect IPC entry {File}", file.FullName);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not delete temporary entry {Path}", path);
        }
    }

    private static bool TryExtractCookieLeasePid(string name, out int pid)
    {
        pid = 0;
        if (!name.StartsWith(DirectoryPrefix, StringComparison.Ordinal))
            return false;

        var suffix = name[DirectoryPrefix.Length..];
        var dashIndex = suffix.IndexOf('-');
        if (dashIndex <= 0 || dashIndex == suffix.Length - 1 ||
            suffix.IndexOf('-', dashIndex + 1) >= 0 ||
            !int.TryParse(suffix.AsSpan(0, dashIndex), out pid) || pid <= 0)
            return false;

        return Guid.TryParseExact(suffix[(dashIndex + 1)..], "N", out _);
    }

    private static bool IsOwnedCookieFile(FileInfo file)
    {
        if (!string.Equals(file.Name, "cookies.txt", StringComparison.Ordinal) ||
            !IsRegularNonSymlink(file))
            return false;

        var directory = file.Directory;
        return directory is not null && TryExtractCookieLeasePid(directory.Name, out _);
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


    private static void DeleteDirectoryRecursively(string directoryPath)
    {
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (IsSafeDirectory(directory))
                TryRemoveOwnedDirectory(directory, DirectoryPrefix);
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
            if (!IsOwnedCookieFile(info))
                return;

            if (info.Length > 0)
            {
                using var stream = OpenCookieFileForWipe(path);
                if (stream is not null)
                {
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
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not overwrite temporary file {CookieFilePath} before delete", path);
        }

        try
        {
            // File.Delete unlinks a link rather than following it, but the ownership check
            // above prevents links and non-regular entries from being selected in the first place.
            if (IsOwnedCookieFile(new FileInfo(path)))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not delete temporary file {CookieFilePath}", path);
        }
    }

    private static FileStream? OpenCookieFileForWipe(string path)
    {
        if (!OperatingSystem.IsLinux())
            return new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);

        // O_NOFOLLOW prevents a check-then-open race from redirecting the overwrite through a
        // link. O_NONBLOCK also ensures an unexpected FIFO cannot stall startup.
        var descriptor = OpenNoFollow(path, OpenWriteOnly | OpenCloseOnExec | OpenNoFollowFlag | OpenNonBlock);
        if (descriptor < 0)
            throw new IOException($"Could not open temporary cookie file (errno {Marshal.GetLastWin32Error()}).");

        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            return new FileStream(handle, FileAccess.Write, 4096, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private const int OpenWriteOnly = 1;
    private const int OpenNonBlock = 0x800;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenNoFollowFlag = 0x20000;
    private const uint UnixFileTypeMask = 0xF000;
    private const uint UnixRegularFile = 0x8000;
    private const uint UnixSocket = 0xC000;

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct UnixStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong LinkCount;
        public uint Mode;
        public uint UserId;
        public uint GroupId;
        public uint Padding;
    }

    [LibraryImport("libc", EntryPoint = "lstat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStat(string path, out UnixStat stat);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenNoFollow(string path, int flags);

}