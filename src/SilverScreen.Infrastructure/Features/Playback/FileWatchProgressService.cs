using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
namespace SilverScreen.Infrastructure.Features.Playback;

internal sealed record WatchProgressEntry(double Highest, double? Resume);

/// <summary>Persists per-video watch progress locally so cards can reflect playback across launches.</summary>
public sealed class FileWatchProgressService : IWatchProgressService
{
    private static readonly ILogger Logger = Log.ForContext<FileWatchProgressService>();
    private const double CompletionThreshold = 0.9;
    private const double MinimumVisibleFraction = 0.01;
    private const double MinimumVisibleSeconds = 2;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, WatchProgressEntry> _progress;

    public FileWatchProgressService() : this(GetDefaultFilePath())
    {
    }

    internal FileWatchProgressService(string filePath)
    {
        _filePath = filePath;
        _progress = Load(filePath);
    }

    public event EventHandler<WatchProgress>? ProgressChanged;

    public double? GetFraction(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        lock (_lock)
        {
            var fraction = _progress.GetValueOrDefault(videoId)?.Highest;
            return fraction is > 0 ? fraction : null;
        }
    }

    public double? GetResumeFraction(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        lock (_lock)
        {
            var fraction = _progress.GetValueOrDefault(videoId)?.Resume;
            return fraction is > MinimumVisibleFraction and < CompletionThreshold ? fraction : null;
        }
    }

    public void Update(PlaybackRequest request, PlaybackPresenceState state)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (state.PlaylistIndex is < 0 or >= int.MaxValue || state.PlaylistIndex >= request.Videos.Length ||
            state.Duration <= TimeSpan.Zero)
            return;

        var video = request.Videos[state.PlaylistIndex];
        var fraction = Math.Clamp(state.Position.TotalSeconds / state.Duration.TotalSeconds, 0, 1);
        if (state.Position < TimeSpan.FromSeconds(MinimumVisibleSeconds) || fraction < MinimumVisibleFraction)
        {
            ClearResumePosition(video.Id);
            return;
        }

        var completed = fraction >= CompletionThreshold || state.Duration - state.Position <= TimeSpan.FromSeconds(30);
        if (completed)
            fraction = 1;

        WatchProgress? changed = null;
        lock (_lock)
        {
            var existing = _progress.GetValueOrDefault(video.Id) ?? new WatchProgressEntry(0, null);
            var cardChanged = fraction > existing.Highest &&
                              Math.Floor(fraction * 100) != Math.Floor(existing.Highest * 100);
            var highest = cardChanged ? fraction : existing.Highest;
            double? resume = completed ? null : fraction;
            if (highest == existing.Highest && resume == existing.Resume)
                return;

            _progress[video.Id] = new WatchProgressEntry(highest, resume);
            Logger.Debug("Updated watch progress for video {VideoId} to {Fraction:P1}", video.Id, highest);
            WriteAtomically(_progress);
            if (cardChanged)
                changed = new WatchProgress(video.Id, highest);
        }

        if (changed is not null)
            ProgressChanged?.Invoke(this, changed);
    }

    private void ClearResumePosition(string videoId)
    {
        lock (_lock)
        {
            if (!_progress.TryGetValue(videoId, out var existing) || existing.Resume is null)
                return;

            _progress[videoId] = existing with { Resume = null };
            WriteAtomically(_progress);
        }
    }

    private static Dictionary<string, WatchProgressEntry> Load(string filePath)
    {
        string json;
        try
        {
            if (!File.Exists(filePath)) return [];
            json = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "Failed to load watch progress from {FilePath}", filePath);
            return [];
        }

        try
        {
            var map = JsonSerializer.Deserialize(json, WatchProgressJsonContext.Default.WatchProgressEntries);
            if (map is not null)
            {
                Logger.Information("Loaded watch progress for {Count} videos from {FilePath}", map.Count, filePath);
                return map;
            }
        }
        catch (JsonException)
        {
            // Try the pre-resume legacy format below.
        }

        try
        {
            var legacy = JsonSerializer.Deserialize(json, WatchProgressJsonContext.Default.LegacyWatchProgressMap);
            if (legacy is null)
                return [];

            var migrated = legacy.ToDictionary(
                static pair => pair.Key,
                static pair => new WatchProgressEntry(pair.Value, pair.Value));
            Logger.Information("Loaded legacy watch progress for {Count} videos from {FilePath}", migrated.Count, filePath);
            return migrated;
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to load watch progress from {FilePath}", filePath);
            return [];
        }
    }

    private void WriteAtomically(Dictionary<string, WatchProgressEntry> progress)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(progress, WatchProgressJsonContext.Default.WatchProgressEntries));
            File.Move(temporaryPath, _filePath, true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save watch progress to {FilePath}", _filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
    private static string GetDefaultFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome))
            return Path.Combine(configHome, "SilverScreen", "watch-progress.json");

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDirectory = string.IsNullOrWhiteSpace(userHome) ? Path.GetTempPath() : Path.Combine(userHome, ".config");
        return Path.Combine(configDirectory, "SilverScreen", "watch-progress.json");
    }
}
