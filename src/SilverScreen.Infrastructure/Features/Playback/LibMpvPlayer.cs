using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Serilog;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed record LibMpvSubtitleTrack(long Id, string Language, string Label, bool IsSelected);

public sealed record LibMpvChapter(TimeSpan Start, string Title);

public sealed record LibMpvPlaybackState(
    int PlaylistIndex,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPaused,
    bool IsMuted,
    double Volume,
    double Speed,
    bool IsSeekable,
    bool HasMedia,
    bool IsLoading,
    IReadOnlyList<LibMpvSubtitleTrack> SubtitleTracks,
    IReadOnlyList<LibMpvChapter> Chapters);

public sealed class LibMpvPlayer : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<LibMpvPlayer>();
    private readonly Task? _commandPump;

    private readonly Channel<Action> _commands = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Action<Action> _dispatch;
    private readonly Task? _eventPump;
    private readonly Lock _gate = new();
    private readonly ILibMpvNativeApi _native;
    private string? _cookieFilePath;
    private nint _handle;
    private AppPreferences? _preferences;
    private string _quality = "Best";
    private ReloadSnapshot? _reload;
    private nint _renderContext;
    private PlaybackRequest? _request;
    private bool _resumeAfterRenderer;

    private LibMpvPlaybackState _state = new(-1, TimeSpan.Zero, TimeSpan.Zero, true, false, 100, 1, false, false,
        false, [], []);

    private GCHandle _updateCallbackHandle;

    public LibMpvPlayer(Action<Action> dispatch) : this(new LibMpvNative(), dispatch)
    {
    }

    internal LibMpvPlayer(ILibMpvNativeApi native, Action<Action> dispatch)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        IsAvailable = native.IsAvailable;
        AvailabilityError = native.AvailabilityError;
        if (!IsAvailable)
        {
            Logger.Warning("libmpv native library is not available: {Error}", AvailabilityError);
            return;
        }
        try
        {
            _handle = native.Create();
            if (_handle == 0) throw new LibMpvException("mpv_create returned no handle.");
            Check(native.SetOptionString(_handle, "config", "no"));
            Check(native.SetOptionString(_handle, "vo", "libmpv"));
            Check(native.SetOptionString(_handle, "hwdec", "auto-safe"));
            Check(native.Initialize(_handle));
            Observe("time-pos", LibMpvFormat.Double);
            Observe("duration", LibMpvFormat.Double);
            Observe("pause", LibMpvFormat.Flag);
            Observe("volume", LibMpvFormat.Double);
            Observe("mute", LibMpvFormat.Flag);
            Observe("speed", LibMpvFormat.Double);
            Observe("seekable", LibMpvFormat.Flag);
            Observe("playlist-pos", LibMpvFormat.Int64);
            Observe("path", LibMpvFormat.String);
            Observe("sid", LibMpvFormat.Int64);
            _commandPump = Task.Run(PumpCommandsAsync);
            _eventPump = Task.Run(PumpEvents);
            Logger.Information("LibMpvPlayer initialized successfully");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to initialize LibMpvPlayer handle or options");
            AvailabilityError = exception.Message;
            IsAvailable = false;
            if (_handle != 0) native.Destroy(_handle);
            _handle = 0;
        }
    }

    public bool IsAvailable { get; }
    public string? AvailabilityError { get; private set; }

    private bool IsDisposing { get; set; }

    public void Dispose()
    {
        if (IsDisposing) return;
        IsDisposing = true;
        if (IsAvailable && _handle != 0)
            try
            {
                _native.Command(_handle, "stop");
            }
            catch
            {
                // ignored
            }

        _commands.Writer.TryComplete();
        try
        {
            _commandPump?.GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }

        if (_handle != 0) _native.Wakeup(_handle);
        try
        {
            _eventPump?.GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }

        nint renderContext;
        lock (_gate)
        {
            renderContext = _renderContext;
            _renderContext = 0;
        }

        if (renderContext != 0)
        {
            _native.SetRenderUpdateCallback(renderContext, 0, 0);
            if (_updateCallbackHandle.IsAllocated) _updateCallbackHandle.Free();
            _native.FreeRenderContext(renderContext);
        }

        if (_updateCallbackHandle.IsAllocated) _updateCallbackHandle.Free();
        if (_handle != 0) _native.Destroy(_handle);
        _handle = 0;
        _native.Dispose();
    }

    public event EventHandler? RenderRequested;
    public event EventHandler<LibMpvPlaybackState>? StateChanged;
    public event EventHandler<string>? PlaybackFailed;

    public unsafe void InitializeRenderer()
    {
        if (!IsAvailable || IsDisposing) return;
        try
        {
            lock (_gate)
            {
                if (_renderContext != 0) return;
                Check(_native.CreateRenderContext(out _renderContext, _handle));
                _updateCallbackHandle = GCHandle.Alloc(this);
                _native.SetRenderUpdateCallback(_renderContext,
                    (nint)(delegate* unmanaged[Cdecl]<nint, void>)&RenderUpdateCallback,
                    GCHandle.ToIntPtr(_updateCallbackHandle));
            }

            if (!_resumeAfterRenderer) return;
            _resumeAfterRenderer = false;
            Enqueue(LoadCurrentRequest);
        }
        catch (Exception exception)
        {
            PublishFailure(exception.Message);
        }
    }

    public void Load(PlaybackRequest request, AppPreferences preferences, string? cookieFilePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preferences);
        _ = MpvCommandBuilder.GetPlaybackUrls(request);
        lock (_gate)
        {
            if (IsDisposing || !IsAvailable) return;
            _request = request;
            _preferences = preferences;
            _cookieFilePath = cookieFilePath;
            _quality = MpvCommandBuilder.BuildYtdlFormat(preferences.VideoQuality) is null
                ? "Best"
                : preferences.VideoQuality;
            _reload = null;
            _state = _state with { IsPaused = false, IsLoading = true, SubtitleTracks = [], Chapters = [] };
        }

        var primaryVideo = request.Videos.FirstOrDefault();
        Logger.Information("Loading playback request for video {VideoId} ({Title}) with {Count} item(s) at quality {Quality}", primaryVideo?.Id, primaryVideo?.Title, request.Videos.Length, _quality);
        PublishState();

        Enqueue(LoadCurrentRequest);
    }

    public void SetPaused(bool paused)
    {
        Enqueue(() => Check(_native.SetPropertyFlag(_handle, "pause", paused)));
    }

    public void TogglePause()
    {
        Enqueue(() => Check(_native.Command(_handle, "cycle", "pause")));
    }

    public void ToggleMute()
    {
        Enqueue(() => Check(_native.Command(_handle, "cycle", "mute")));
    }

    public void StepFrame(bool forward)
    {
        Enqueue(() => Check(_native.Command(_handle, forward ? "frame-step" : "frame-back-step")));
    }

    public void AdjustVolume(double amount)
    {
        Enqueue(() => Check(_native.Command(_handle, "add", "volume",
            amount.ToString(CultureInfo.InvariantCulture))));
    }

    public void MovePlaylist(bool forward)
    {
        Enqueue(() => Check(_native.Command(_handle, forward ? "playlist-next" : "playlist-prev")));
    }

    public void PlayPlaylistIndex(int index)
    {
        Enqueue(() => Check(_native.Command(_handle, "playlist-play-index", index.ToString(CultureInfo.InvariantCulture))));
    }

    public void RemovePlaylistItem(int index)
    {
        Enqueue(() => Check(_native.Command(_handle, "playlist-remove", index.ToString(CultureInfo.InvariantCulture))));
    }

    public void MovePlaylistItem(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        var mpvTarget = toIndex > fromIndex ? toIndex + 1 : toIndex;
        Enqueue(() => Check(_native.Command(_handle, "playlist-move",
            fromIndex.ToString(CultureInfo.InvariantCulture),
            mpvTarget.ToString(CultureInfo.InvariantCulture))));
    }

    public void AppendPlaylistItem(string url)
    {
        Enqueue(() => Check(_native.Command(_handle, "loadfile", url, "append-play")));
    }

    public void SeekRelative(double seconds)
    {
        Enqueue(() => Check(_native.Command(_handle, "seek",
            seconds.ToString(CultureInfo.InvariantCulture), "relative+exact")));
    }

    public void SeekAbsolute(double seconds, bool exact = true)
    {
        Enqueue(() => Check(_native.Command(_handle, "seek",
            seconds.ToString(CultureInfo.InvariantCulture), exact ? "absolute+exact" : "absolute+keyframes")));
    }

    public void SetVolume(double volume)
    {
        Enqueue(() => Check(_native.SetPropertyDouble(_handle, "volume", Math.Clamp(volume, 0, 100))));
    }

    public void SetSpeed(double speed)
    {
        Enqueue(() => Check(_native.SetPropertyDouble(_handle, "speed", speed)));
    }

    public void SelectSubtitleTrack(long trackId)
    {
        Enqueue(() => Check(_native.SetPropertyString(_handle, "sid",
            trackId <= 0 ? "no" : trackId.ToString(CultureInfo.InvariantCulture))));
    }

    public void SetQuality(string quality)
    {
        if (quality is not ("Best" or "1080p" or "720p" or "480p" or "360p"))
            throw new ArgumentOutOfRangeException(nameof(quality));

        lock (_gate)
        {
            if (IsDisposing || !IsAvailable) return;
            _quality = quality;
            if (!_state.HasMedia || _request is null) return;
            _reload = new ReloadSnapshot(_state.PlaylistIndex, _state.Position, _state.IsPaused, _state.Volume,
                _state.Speed);
        }

        Enqueue(LoadCurrentRequest);
    }

    public void Render(int width, int height)
    {
        if (width <= 0 || height <= 0 || IsDisposing) return;
        nint renderContext;
        lock (_gate)
        {
            renderContext = _renderContext;
        }

        if (renderContext == 0) return;

        try
        {
            Check(_native.Render(renderContext, _native.GetFramebufferBinding(), width, height));
        }
        catch (Exception exception)
        {
            PublishFailure(exception.Message);
        }
    }

    public void ShutdownRenderer()
    {
        nint renderContext;
        lock (_gate)
        {
            if (_renderContext == 0) return;
            renderContext = _renderContext;
            _renderContext = 0;
            _resumeAfterRenderer = _state.HasMedia;
        }

        SetPaused(true);
        _native.SetRenderUpdateCallback(renderContext, 0, 0);
        if (_updateCallbackHandle.IsAllocated) _updateCallbackHandle.Free();
        _native.FreeRenderContext(renderContext);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (IsDisposing || !IsAvailable) return;
            _reload = null;
            _resumeAfterRenderer = false;
            _state = _state with
            {
                PlaylistIndex = -1,
                Position = TimeSpan.Zero,
                Duration = TimeSpan.Zero,
                IsPaused = true,
                IsSeekable = false,
                HasMedia = false,
                IsLoading = false,
                SubtitleTracks = [],
                Chapters = []
            };
        }

        PublishState();
        Enqueue(() => Check(_native.Command(_handle, "stop")));
    }

    private async Task PumpCommandsAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            if (IsDisposing) continue;
            try
            {
                command();
            }
            catch (Exception exception)
            {
                PublishFailure(exception.Message);
            }
        }
    }

    private void PumpEvents()
    {
        while (!IsDisposing && _handle != 0)
        {
            var mpvEvent = _native.WaitEvent(_handle, -1);
            switch (mpvEvent.EventId)
            {
                case (int)LibMpvEventId.None:
                    continue;
                case (int)LibMpvEventId.Shutdown:
                    return;
                default:
                    try
                    {
                        HandleEvent(mpvEvent);
                    }
                    catch (Exception exception)
                    {
                        PublishFailure(exception.Message);
                    }

                    break;
            }
        }
    }

    private void HandleEvent(LibMpvEvent mpvEvent)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch ((LibMpvEventId)mpvEvent.EventId)
        {
            case LibMpvEventId.PropertyChange:
                HandlePropertyChange(mpvEvent.Data);
                break;
            case LibMpvEventId.FileLoaded:
                HandleFileLoaded();
                break;
            case LibMpvEventId.EndFile:
                HandleEndFile(Marshal.PtrToStructure<LibMpvEventEndFile>(mpvEvent.Data));
                break;
        }
    }

    private void HandlePropertyChange(nint data)
    {
        var property = Marshal.PtrToStructure<LibMpvEventProperty>(data);
        var name = Marshal.PtrToStringUTF8(property.Name);
        if (name is null || property.Format == LibMpvFormat.None || property.Data == 0) return;

        lock (_gate)
        {
            _state = name switch
            {
                "time-pos" when property.Format == LibMpvFormat.Double => _state with
                {
                    Position = ToTimeSpan(Marshal.PtrToStructure<double>(property.Data))
                },
                "duration" when property.Format == LibMpvFormat.Double => _state with
                {
                    Duration = ToTimeSpan(Marshal.PtrToStructure<double>(property.Data))
                },
                "pause" when property.Format == LibMpvFormat.Flag => _state with
                {
                    IsPaused = Marshal.ReadInt32(property.Data) != 0
                },
                "mute" when property.Format == LibMpvFormat.Flag => _state with
                {
                    IsMuted = Marshal.ReadInt32(property.Data) != 0
                },
                "volume" when property.Format == LibMpvFormat.Double => _state with
                {
                    Volume = Marshal.PtrToStructure<double>(property.Data)
                },
                "speed" when property.Format == LibMpvFormat.Double => _state with
                {
                    Speed = Marshal.PtrToStructure<double>(property.Data)
                },
                "seekable" when property.Format == LibMpvFormat.Flag => _state with
                {
                    IsSeekable = Marshal.ReadInt32(property.Data) != 0
                },
                "playlist-pos" when property.Format == LibMpvFormat.Int64 => _state with
                {
                    PlaylistIndex = checked((int)Marshal.ReadInt64(property.Data))
                },
                "sid" when property.Format == LibMpvFormat.Int64 => _state with
                {
                    SubtitleTracks = SelectSubtitleTrack(_state.SubtitleTracks, Marshal.ReadInt64(property.Data))
                },
                _ => _state
            };
        }

        PublishState();
    }

    private void HandleFileLoaded()
    {
        var subtitleTracks = ReadSubtitleTracks();
        var chapters = ReadChapters();
        ReloadSnapshot? reload;
        lock (_gate)
        {
            _state = _state with
            {
                HasMedia = true,
                IsLoading = false,
                SubtitleTracks = subtitleTracks,
                Chapters = chapters
            };
            reload = _reload;
        }

        if (reload is not null && reload.PlaylistIndex == _state.PlaylistIndex)
        {
            _reload = null;
            Enqueue(() =>
            {
                Check(_native.SetPropertyDouble(_handle, "volume", reload.Volume));
                Check(_native.SetPropertyDouble(_handle, "speed", reload.Speed));
                Check(_native.Command(_handle, "seek",
                    reload.Position.TotalSeconds.ToString(CultureInfo.InvariantCulture), "absolute+exact"));
                Check(_native.SetPropertyFlag(_handle, "pause", reload.IsPaused));
            });
        }

        PublishState();
    }

    private void HandleEndFile(LibMpvEventEndFile endFile)
    {
        if (endFile.Reason == LibMpvEndFileReason.Error)
            PublishFailure(_native.ErrorString(endFile.Error));
    }

    private void LoadCurrentRequest()
    {
        PlaybackRequest request;
        string quality;
        string? cookieFilePath;
        AppPreferences preferences;
        ReloadSnapshot? reload;
        lock (_gate)
        {
            if (_request is null || _preferences is null) return;
            request = _request;
            quality = _quality;
            cookieFilePath = _cookieFilePath;
            preferences = _preferences;
            reload = _reload;
        }

        var urls = MpvCommandBuilder.GetPlaybackUrls(request);
        var rawOptions = BuildYtdlRawOptions(cookieFilePath,
            preferences.MarkWatchedVideos && !preferences.YouTubePlaybackTelemetryEnabled);
        Check(_native.SetPropertyString(_handle, "ytdl-raw-options", rawOptions));
        Check(_native.SetPropertyString(_handle, "keep-open",
            preferences.AutoAdvanceNextVideo ? "yes" : "always"));
        Check(_native.SetPropertyString(_handle, "ytdl-format",
            MpvCommandBuilder.BuildYtdlFormat(quality) ?? string.Empty));
        Check(_native.Command(_handle, "loadfile", urls[0], "replace"));
        foreach (var url in urls.Skip(1)) Check(_native.Command(_handle, "loadfile", url, "append-play"));
        if (reload is not null) Check(_native.SetPropertyInt64(_handle, "playlist-pos", reload.PlaylistIndex));
    }

    private List<LibMpvSubtitleTrack> ReadSubtitleTracks()
    {
        if (!int.TryParse(_native.GetPropertyString(_handle, "track-list/count"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackCount) ||
            trackCount <= 0)
            return [];

        var selectedTrackId = long.TryParse(_native.GetPropertyString(_handle, "sid"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
        var subtitleTracks = new List<LibMpvSubtitleTrack>();
        for (var index = 0; index < trackCount; index++)
        {
            var propertyPrefix = $"track-list/{index}";
            if (!string.Equals(_native.GetPropertyString(_handle, $"{propertyPrefix}/type"), "sub",
                    StringComparison.Ordinal) ||
                !long.TryParse(_native.GetPropertyString(_handle, $"{propertyPrefix}/id"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackId))
                continue;

            var title = _native.GetPropertyString(_handle, $"{propertyPrefix}/title");
            var language = _native.GetPropertyString(_handle, $"{propertyPrefix}/lang");
            var label = string.IsNullOrWhiteSpace(title)
                ? language
                : string.IsNullOrWhiteSpace(language) ||
                  string.Equals(title, language, StringComparison.OrdinalIgnoreCase)
                    ? title
                    : $"{title} ({language})";
            subtitleTracks.Add(new LibMpvSubtitleTrack(trackId, language ?? label ?? $"Subtitle {trackId}",
                label ?? $"Subtitle {trackId}", trackId == selectedTrackId));
        }

        return subtitleTracks;
    }

    private List<LibMpvChapter> ReadChapters()
    {
        if (!int.TryParse(_native.GetPropertyString(_handle, "chapter-list/count"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapterCount) ||
            chapterCount <= 0)
            return [];

        var chapters = new List<LibMpvChapter>(chapterCount);
        for (var index = 0; index < chapterCount; index++)
        {
            var propertyPrefix = $"chapter-list/{index}";
            if (!double.TryParse(_native.GetPropertyString(_handle, $"{propertyPrefix}/time"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                continue;

            var title = _native.GetPropertyString(_handle, $"{propertyPrefix}/title");
            chapters.Add(new LibMpvChapter(TimeSpan.FromSeconds(seconds),
                string.IsNullOrWhiteSpace(title) ? $"Chapter {index + 1}" : title));
        }

        return chapters;
    }

    private static LibMpvSubtitleTrack[] SelectSubtitleTrack(
        IReadOnlyList<LibMpvSubtitleTrack> tracks, long selectedTrackId)
    {
        return [.. tracks.Select(track => track with { IsSelected = track.Id == selectedTrackId })];
    }

    private static string BuildYtdlRawOptions(string? cookieFilePath, bool markWatchedVideos)
    {
        var options = new List<string>
        {
            "write-subs=", "write-auto-subs=", "sub-langs=all", "sub-format=vtt"
        };
        if (!string.IsNullOrWhiteSpace(cookieFilePath)) options.Insert(0, $"cookies={cookieFilePath}");
        if (markWatchedVideos) options.Add("mark-watched=");
        return string.Join(',', options);
    }

    private void Observe(string name, LibMpvFormat format)
    {
        Check(_native.ObserveProperty(_handle, 0, name, format));
    }

    private void Enqueue(Action action)
    {
        if (!IsDisposing && !_commands.Writer.TryWrite(action)) PublishFailure("The embedded player is shutting down.");
    }

    private void PublishState()
    {
        LibMpvPlaybackState snapshot;
        lock (_gate)
        {
            snapshot = _state;
        }

        Dispatch(() => StateChanged?.Invoke(this, snapshot));
    }

    private void PublishFailure(string detail)
    {
        Logger.Error("LibMpv playback failure: {Detail}", detail);
        Dispatch(() => PlaybackFailed?.Invoke(this, detail));
    }

    private void Dispatch(Action action)
    {
        if (IsDisposing) return;
        _dispatch(() =>
        {
            if (!IsDisposing) action();
        });
    }

    private void Check(int result)
    {
        if (result < 0) throw new LibMpvException(_native.ErrorString(result));
    }

    private static TimeSpan ToTimeSpan(double seconds)
    {
        return seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(seconds);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RenderUpdateCallback(nint context)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is LibMpvPlayer { IsDisposing: false } player)
                player.Dispatch(() => player.RenderRequested?.Invoke(player, EventArgs.Empty));
        }
        catch
        {
            // ignored
        }
    }

    private sealed record ReloadSnapshot(
        int PlaylistIndex,
        TimeSpan Position,
        bool IsPaused,
        double Volume,
        double Speed);

    private sealed class LibMpvException(string message) : Exception(message);
}