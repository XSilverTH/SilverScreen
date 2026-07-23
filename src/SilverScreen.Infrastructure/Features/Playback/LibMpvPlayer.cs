using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed record LibMpvPlaybackState(
    int PlaylistIndex,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPaused,
    double Volume,
    double Speed,
    bool IsSeekable,
    bool HasMedia,
    bool IsLoading);

public sealed class LibMpvPlayer : IDisposable
{
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
    private long _generation;
    private nint _handle;
    private AppPreferences? _preferences;
    private string _quality = "Best";
    private ReloadSnapshot? _reload;
    private nint _renderContext;
    private PlaybackRequest? _request;
    private bool _resumeAfterRenderer;
    private LibMpvPlaybackState _state = new(-1, TimeSpan.Zero, TimeSpan.Zero, true, 100, 1, false, false, false);
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
        if (!IsAvailable) return;

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
            Observe("speed", LibMpvFormat.Double);
            Observe("seekable", LibMpvFormat.Flag);
            Observe("playlist-pos", LibMpvFormat.Int64);
            Observe("path", LibMpvFormat.String);
            _commandPump = Task.Run(PumpCommandsAsync);
            _eventPump = Task.Run(PumpEvents);
        }
        catch (Exception exception)
        {
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
            }

        _commands.Writer.TryComplete();
        try
        {
            _commandPump?.GetAwaiter().GetResult();
        }
        catch
        {
        }

        if (_handle != 0) _native.Wakeup(_handle);
        try
        {
            _eventPump?.GetAwaiter().GetResult();
        }
        catch
        {
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
    public event EventHandler? PlaybackEnded;

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

            if (_resumeAfterRenderer)
            {
                _resumeAfterRenderer = false;
                Enqueue(LoadCurrentRequest);
            }
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
            _generation++;
            _request = request;
            _preferences = preferences;
            _cookieFilePath = cookieFilePath;
            _quality = MpvCommandBuilder.BuildYtdlFormat(preferences.VideoQuality) is null
                ? "Best"
                : preferences.VideoQuality;
            _reload = null;
            _state = _state with { IsLoading = true };
        }

        PublishState();

        Enqueue(LoadCurrentRequest);
    }

    public void SetPaused(bool paused)
    {
        Enqueue(() => Check(_native.SetPropertyFlag(_handle, "pause", paused)));
    }

    public void SeekRelative(double seconds)
    {
        Enqueue(() => Check(_native.Command(_handle, "seek",
            seconds.ToString(CultureInfo.InvariantCulture), "relative+exact")));
    }

    public void SeekAbsolute(double seconds)
    {
        Enqueue(() => Check(_native.Command(_handle, "seek",
            seconds.ToString(CultureInfo.InvariantCulture), "absolute+exact")));
    }

    public void SetVolume(double volume)
    {
        Enqueue(() => Check(_native.SetPropertyDouble(_handle, "volume", Math.Clamp(volume, 0, 100))));
    }

    public void SetSpeed(double speed)
    {
        Enqueue(() => Check(_native.SetPropertyDouble(_handle, "speed", speed)));
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
            _generation++;
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
            _generation++;
            _reload = null;
            _resumeAfterRenderer = false;
            _state = _state with
            {
                PlaylistIndex = -1, Position = TimeSpan.Zero, Duration = TimeSpan.Zero, IsPaused = true,
                IsSeekable = false, HasMedia = false, IsLoading = false
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
            if (mpvEvent.EventId == (int)LibMpvEventId.None) continue;
            if (mpvEvent.EventId == (int)LibMpvEventId.Shutdown) return;
            try
            {
                HandleEvent(mpvEvent);
            }
            catch (Exception exception)
            {
                PublishFailure(exception.Message);
            }
        }
    }

    private void HandleEvent(LibMpvEvent mpvEvent)
    {
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
                _ => _state
            };
        }

        PublishState();
    }

    private void HandleFileLoaded()
    {
        ReloadSnapshot? reload;
        lock (_gate)
        {
            _state = _state with { HasMedia = true, IsLoading = false };
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
        {
            PublishFailure(_native.ErrorString(endFile.Error));
            return;
        }

        if (endFile.Reason != LibMpvEndFileReason.Eof) return;
        bool ended;
        lock (_gate)
        {
            var itemCount = _request?.Videos.Length ?? 0;
            ended = _state.PlaylistIndex >= itemCount - 1;
            if (ended)
                _state = _state with { HasMedia = false, IsPaused = true, Position = TimeSpan.Zero, IsLoading = false };
        }

        if (!ended) return;
        PublishState();
        Dispatch(() => PlaybackEnded?.Invoke(this, EventArgs.Empty));
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
        var rawOptions = string.IsNullOrWhiteSpace(cookieFilePath)
            ? string.Empty
            : $"cookies={cookieFilePath}" + (preferences.MarkWatchedVideos ? ",mark-watched=" : string.Empty);
        Check(_native.SetPropertyString(_handle, "ytdl-raw-options", rawOptions));
        Check(_native.SetPropertyString(_handle, "ytdl-format",
            MpvCommandBuilder.BuildYtdlFormat(quality) ?? string.Empty));
        Check(_native.Command(_handle, "loadfile", urls[0], "replace"));
        foreach (var url in urls.Skip(1)) Check(_native.Command(_handle, "loadfile", url, "append-play"));
        if (reload is not null) Check(_native.SetPropertyInt64(_handle, "playlist-pos", reload.PlaylistIndex));
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
            if (GCHandle.FromIntPtr(context).Target is LibMpvPlayer player && !player.IsDisposing)
                player.Dispatch(() => player.RenderRequested?.Invoke(player, EventArgs.Empty));
        }
        catch
        {
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