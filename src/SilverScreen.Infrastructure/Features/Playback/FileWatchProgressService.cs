using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
namespace SilverScreen.Infrastructure.Features.Playback;

/// <summary>Persists per-video watch progress locally so cards can reflect playback across launches.</summary>
public sealed class FileWatchProgressService : IWatchProgressService
{
    private static readonly ILogger Logger = Log.ForContext<FileWatchProgressService>();
    private const double CompletionThreshold = 0.9;
    private const double MinimumVisibleFraction = 0.01;
    private const double MinimumVisibleSeconds = 2;
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, double> _fractions;

    public FileWatchProgressService() : this(GetDefaultFilePath())
    {
    }

    internal FileWatchProgressService(string filePath)
    {
        _filePath = filePath;
        _fractions = Load(filePath);
    }

    public event EventHandler<WatchProgress>? ProgressChanged;

    public double? GetFraction(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        lock (_lock)
            return _fractions.GetValueOrDefault(videoId) is var fraction && fraction > 0 ? fraction : null;
    }

    public void Update(PlaybackRequest request, PlaybackPresenceState state)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (state.PlaylistIndex is < 0 or >= int.MaxValue || state.PlaylistIndex >= request.Videos.Length ||
            state.Duration <= TimeSpan.Zero || state.Position < TimeSpan.FromSeconds(MinimumVisibleSeconds))
            return;

        var video = request.Videos[state.PlaylistIndex];
        var fraction = Math.Clamp(state.Position.TotalSeconds / state.Duration.TotalSeconds, 0, 1);
        if (fraction < MinimumVisibleFraction)
            return;
        if (fraction >= CompletionThreshold || state.Duration - state.Position <= TimeSpan.FromSeconds(30))
            fraction = 1;

        WatchProgress? changed = null;
        lock (_lock)
        {
            var existing = _fractions.GetValueOrDefault(video.Id);
            if (fraction <= existing || Math.Floor(fraction * 100) == Math.Floor(existing * 100))
                return;

            _fractions[video.Id] = fraction;
            Logger.Debug("Updated watch progress for video {VideoId} to {Fraction:P1}", video.Id, fraction);
            WriteAtomically(_fractions);
            changed = new WatchProgress(video.Id, fraction);
        }

        ProgressChanged?.Invoke(this, changed);
    }

    private static Dictionary<string, double> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return [];
            var map = JsonSerializer.Deserialize(File.ReadAllText(filePath), WatchProgressJsonContext.Default.WatchProgressMap) ?? [];
            Logger.Information("Loaded watch progress for {Count} videos from {FilePath}", map.Count, filePath);
            return map;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Logger.Warning(ex, "Failed to load watch progress from {FilePath}", filePath);
            return [];
        }
    }

    private void WriteAtomically(Dictionary<string, double> fractions)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(fractions, WatchProgressJsonContext.Default.WatchProgressMap));
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
