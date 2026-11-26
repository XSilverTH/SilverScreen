using System.Text.Json;
using Serilog;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Downloads;

/// <summary>
///     Manages the offline Library index stored as a hidden minified JSON file
///     (<c>.silverscreen-library.json</c>) inside the configured Library folder,
///     alongside cached thumbnails in <c>.silverscreen-thumbs/</c>.
/// </summary>
public sealed class JsonLibraryService : ILibraryService, IDisposable
{
    public const string IndexFileName = ".silverscreen-library.json";
    public const string ThumbnailsDirectoryName = ".silverscreen-thumbs";
    private const int CurrentSchemaVersion = 1;
    private static readonly ILogger Logger = Log.ForContext<JsonLibraryService>();

    private readonly Dictionary<string, LibraryEntry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private bool _disposed;
    private string _libraryDirectory;

    public JsonLibraryService(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _libraryDirectory = ResolveDirectory(_preferencesService.GetPreferences().LibraryDirectory);
        LoadFromDiskLocked();
        _preferencesService.PreferencesChanged += OnPreferencesChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preferencesService.PreferencesChanged -= OnPreferencesChanged;
    }

    public string LibraryDirectory
    {
        get
        {
            lock (_lock)
            {
                return _libraryDirectory;
            }
        }
    }

    public IReadOnlyList<LibraryEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.Values
                    .OrderByDescending(e => e.DownloadedAt)
                    .ToArray();
            }
        }
    }

    public event EventHandler? LibraryChanged;

    public LibraryEntry? TryGetEntry(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        lock (_lock)
        {
            return _entries.GetValueOrDefault(videoId);
        }
    }

    public LibraryEntry? TryGetPlayableEntry(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        LibraryEntry? entry;
        string libraryDir;
        lock (_lock)
        {
            if (!_entries.TryGetValue(videoId, out entry))
                return null;
            libraryDir = _libraryDirectory;
        }

        return entry.IsFileAvailable(libraryDir) ? entry : null;
    }

    public bool ContainsPlayable(string videoId)
    {
        return TryGetPlayableEntry(videoId) is not null;
    }

    public string? TryGetThumbnailPath(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        LibraryEntry? entry;
        string libraryDir;
        lock (_lock)
        {
            if (!_entries.TryGetValue(videoId, out entry))
                return null;
            libraryDir = _libraryDirectory;
        }

        var path = entry.ResolveThumbnailPath(libraryDir);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    public Task UpsertAsync(LibraryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _entries[entry.Video.Id] = entry;
            WriteToDiskLocked();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string videoId, bool deleteFileFromDisk = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return Task.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();

        LibraryEntry? removed;
        string libraryDir;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.Remove(videoId, out removed))
                return Task.CompletedTask;

            libraryDir = _libraryDirectory;
            WriteToDiskLocked();
        }

        if (deleteFileFromDisk)
        {
            TryDeleteFile(removed.ResolveFilePath(libraryDir));
            if (removed.ResolveThumbnailPath(libraryDir) is { } thumbPath)
                TryDeleteFile(thumbPath);
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _libraryDirectory = ResolveDirectory(_preferencesService.GetPreferences().LibraryDirectory);
            LoadFromDiskLocked();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    internal string GetThumbnailsDirectory()
    {
        return Path.Combine(LibraryDirectory, ThumbnailsDirectoryName);
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        var nextDir = ResolveDirectory(preferences.LibraryDirectory);
        var changed = false;
        lock (_lock)
        {
            if (_disposed || string.Equals(_libraryDirectory, nextDir, StringComparison.Ordinal))
                return;

            _libraryDirectory = nextDir;
            LoadFromDiskLocked();
            changed = true;
        }

        if (changed)
            LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromDiskLocked()
    {
        _entries.Clear();
        var indexPath = Path.Combine(_libraryDirectory, IndexFileName);
        try
        {
            if (!File.Exists(indexPath))
                return;

            using var stream = new FileStream(
                indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            var index = JsonSerializer.Deserialize(stream, LibraryJsonContext.Default.PersistedLibraryIndex);
            if (index?.Entries is null)
                return;

            foreach (var entry in index.Entries)
                if (!string.IsNullOrWhiteSpace(entry.Video?.Id))
                    _entries[entry.Video.Id] = entry;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to load Library index from {IndexPath}", indexPath);
        }
    }

    private void WriteToDiskLocked()
    {
        Directory.CreateDirectory(_libraryDirectory);
        var indexPath = Path.Combine(_libraryDirectory, IndexFileName);
        var tempPath = Path.Combine(_libraryDirectory, $".silverscreen-library.{Guid.NewGuid():N}.tmp");

        var payload = new PersistedLibraryIndex(
            CurrentSchemaVersion,
            _entries.Values.OrderByDescending(e => e.DownloadedAt).ToArray());

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, payload, LibraryJsonContext.Default.PersistedLibraryIndex);
                stream.Flush(true);
            }

            File.Move(tempPath, indexPath, true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to persist Library index to {IndexPath}", indexPath);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static string ResolveDirectory(string? configuredDirectory)
    {
        var raw = string.IsNullOrWhiteSpace(configuredDirectory)
            ? AppPreferences.GetDefaultLibraryDirectory()
            : configuredDirectory.Trim();
        return Path.GetFullPath(raw);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete file {FilePath}", path);
        }
    }
}