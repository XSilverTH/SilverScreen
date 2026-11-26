using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Player;

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
    IReadOnlyList<LibMpvChapter> Chapters,
    bool IsBuffering = false,
    int BufferingPercent = 0);

public sealed partial class LibMpvPlayer : IDisposable
{
    private const double TimePosEmitDeltaSeconds = 0.20;
    private const double TimePosEmitIntervalMs = 150.0;
    private static readonly ILogger Logger = Log.ForContext<LibMpvPlayer>();

    private readonly Channel<Action> _commands = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly Action<Action> _dispatch;
    private readonly Lock _gate = new();
    private readonly Lock _initLock = new();
    private readonly Lazy<ILibMpvNativeApi> _lazyNative;
    private readonly Lock _seekGate = new();
    private string? _availabilityError;
    private Task? _commandPump;
    private string? _cookieFilePath;
    private Task? _eventPump;
    private string? _ffmpegVersion;
    private nint _handle;
    private bool _initialized;
    private bool _isAvailable = true;
    private string? _lastErrorMessage;
    private TimeSpan _lastEmittedPosition;
    private long _lastTimePosEmitTimestamp;
    private string? _lastReportedHwdec;
    private string? _mpvVersion;
    private (double Seconds, bool Exact)? _pendingSeek;
    private AppPreferences? _preferences;
    private string _quality = "Best";
    private ReloadSnapshot? _reload;
    private nint _renderContext;
    private PlaybackRequest? _request;
    private bool _resumeAfterRenderer;
    private bool _seekQueued;

    private LibMpvPlaybackState _state = new(-1, TimeSpan.Zero, TimeSpan.Zero, true, false, 100, 1, false, false,
        false, [], []);

    private GCHandle _updateCallbackHandle;


    public LibMpvPlayer(Action<Action> dispatch) : this(new Lazy<ILibMpvNativeApi>(static () => new LibMpvNative()),
        dispatch, true)
    {
    }

    internal LibMpvPlayer(ILibMpvNativeApi native, Action<Action> dispatch) : this(
        new Lazy<ILibMpvNativeApi>(() => native), dispatch)
    {
    }

    private LibMpvPlayer(Lazy<ILibMpvNativeApi> lazyNative, Action<Action> dispatch, bool lazy = false)
    {
        _lazyNative = lazyNative ?? throw new ArgumentNullException(nameof(lazyNative));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        if (!lazy) EnsureInitialized();
    }

    private ILibMpvNativeApi Native => _lazyNative.Value;

    public bool IsAvailable =>
        _initialized ? _isAvailable :
        _lazyNative.IsValueCreated ? _lazyNative.Value.IsAvailable :
        LibMpvNative.IsAvailable();

    public string? AvailabilityError =>
        _initialized ? _availabilityError :
        _lazyNative.IsValueCreated ? _lazyNative.Value.AvailabilityError :
        null;

    private bool IsDisposing { get; set; }

    public void Dispose()
    {
        if (IsDisposing) return;
        IsDisposing = true;
        if (_initialized && _isAvailable && _handle != 0)
            try
            {
                Native.Command(_handle, "stop");
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

        if (_handle != 0) Native.Wakeup(_handle);
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
            Native.SetRenderUpdateCallback(renderContext, 0, 0);
            Native.FreeRenderContext(renderContext);
        }

        if (_updateCallbackHandle.IsAllocated) _updateCallbackHandle.Free();
        if (_handle != 0) Native.Destroy(_handle);
        _handle = 0;
        if (_lazyNative.IsValueCreated) _lazyNative.Value.Dispose();
    }

    private void EnsureInitialized()
    {
        if (_initialized || IsDisposing) return;
        lock (_initLock)
        {
            if (_initialized || IsDisposing) return;
            _initialized = true;
            var native = _lazyNative.Value;
            _isAvailable = native.IsAvailable;
            _availabilityError = native.AvailabilityError;
            if (!_isAvailable)
            {
                Logger.Warning("libmpv native library is not available: {Error}", _availabilityError);
                return;
            }

            try
            {
                _handle = native.Create();
                if (_handle == 0) throw new LibMpvException("mpv_create returned no handle.");
                Check(native.SetOptionString(_handle, "config", "no"));
                Check(native.SetOptionString(_handle, "vo", "libmpv"));
                Check(native.SetOptionString(_handle, "hwdec", "auto-safe"));
                Check(native.SetOptionString(_handle, "demuxer-max-bytes", "33554432"));
                Check(native.SetOptionString(_handle, "demuxer-max-back-bytes", "16777216"));
                Check(native.Initialize(_handle));
                Check(native.RequestLogMessages(_handle, "info"));
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
                Observe("paused-for-cache", LibMpvFormat.Flag);
                Observe("cache-buffering-state", LibMpvFormat.Int64);
                Observe("hwdec-current", LibMpvFormat.String);
                _commandPump = Task.Run(PumpCommandsAsync);
                _eventPump = Task.Run(PumpEvents);
                Logger.Debug("LibMpvPlayer initialized successfully");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to initialize LibMpvPlayer handle or options");
                _availabilityError = DiagnosticSanitizer.Sanitize(exception.Message);
                _isAvailable = false;
                if (_handle != 0) native.Destroy(_handle);
                _handle = 0;
            }
        }
    }

    public event EventHandler? RenderRequested;
    public event EventHandler<LibMpvPlaybackState>? StateChanged;
    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<TimeSpan>? StreamDelayDetected;

    public unsafe void InitializeRenderer()
    {
        EnsureInitialized();
        if (!IsAvailable || IsDisposing) return;
        try
        {
            lock (_gate)
            {
                if (_renderContext != 0) return;
                Check(Native.CreateRenderContext(out _renderContext, _handle));
                _updateCallbackHandle = GCHandle.Alloc(this);
                Native.SetRenderUpdateCallback(_renderContext,
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
            _quality = preferences.Quality.ToPersistedString();
            _reload = null;
            _lastErrorMessage = null;
            _lastEmittedPosition = TimeSpan.Zero;
            _lastTimePosEmitTimestamp = 0;
            _state = _state with
            {
                IsPaused = false, IsLoading = true, IsBuffering = false, BufferingPercent = 0, SubtitleTracks = [],
                Chapters = []
            };
        }

        Logger.Information(
            "Loading playback request with {Count} item(s) at quality {Quality}", request.Videos.Length, _quality);
        PublishState();

        Enqueue(LoadCurrentRequest);
    }

    public void SetPaused(bool paused)
    {
        Enqueue(() => Check(Native.SetPropertyFlag(_handle, "pause", paused)));
    }

    public void TogglePause()
    {
        Enqueue(() => Check(Native.Command(_handle, "cycle", "pause")));
    }

    public void ToggleMute()
    {
        Enqueue(() => Check(Native.Command(_handle, "cycle", "mute")));
    }

    public void StepFrame(bool forward)
    {
        Enqueue(() => Check(Native.Command(_handle, forward ? "frame-step" : "frame-back-step")));
    }

    public void AdjustVolume(double amount)
    {
        Enqueue(() => Check(Native.Command(_handle, "add", "volume",
            amount.ToString(CultureInfo.InvariantCulture))));
    }

    public void MovePlaylist(bool forward)
    {
        Enqueue(() => Check(Native.Command(_handle, forward ? "playlist-next" : "playlist-prev")));
    }

    public void PlayPlaylistIndex(int index)
    {
        Enqueue(() =>
            Check(Native.Command(_handle, "playlist-play-index", index.ToString(CultureInfo.InvariantCulture))));
    }


    public void RemovePlaylistItem(int index)
    {
        Enqueue(() => Check(Native.Command(_handle, "playlist-remove", index.ToString(CultureInfo.InvariantCulture))));
    }

    public void MovePlaylistItem(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        var mpvTarget = toIndex > fromIndex ? toIndex + 1 : toIndex;
        Enqueue(() => Check(Native.Command(_handle, "playlist-move",
            fromIndex.ToString(CultureInfo.InvariantCulture),
            mpvTarget.ToString(CultureInfo.InvariantCulture))));
    }

    public void AppendPlaylistItem(string url)
    {
        Enqueue(() => Check(Native.Command(_handle, "loadfile", url, "append-play")));
    }

    /// <summary>
    ///     Updates the request used by renderer reloads without restarting the current
    ///     playlist. Live queue edits are applied to mpv separately by the caller.
    /// </summary>
    public void UpdatePlaylistRequest(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (IsDisposing) return;

            _request = request;
        }
    }


    public void SeekRelative(double seconds)
    {
        Enqueue(() => Check(Native.Command(_handle, "seek",
            seconds.ToString(CultureInfo.InvariantCulture), "relative+exact")));
    }

    public void SeekAbsolute(double seconds, bool exact = true)
    {
        lock (_seekGate)
        {
            _pendingSeek = (seconds, exact);
            if (_seekQueued) return;
            _seekQueued = true;
            Enqueue(DispatchPendingSeek);
        }
    }

    private void DispatchPendingSeek()
    {
        (double Seconds, bool Exact) target;
        lock (_seekGate)
        {
            if (!_pendingSeek.HasValue)
            {
                _seekQueued = false;
                return;
            }

            target = _pendingSeek.Value;
            _pendingSeek = null;
            _seekQueued = false;
        }

        Check(Native.Command(_handle, "seek",
            target.Seconds.ToString(CultureInfo.InvariantCulture),
            target.Exact ? "absolute+exact" : "absolute+keyframes"));
    }

    public void SetVolume(double volume)
    {
        Enqueue(() => Check(Native.SetPropertyDouble(_handle, "volume", Math.Clamp(volume, 0, 100))));
    }

    public void SetSpeed(double speed)
    {
        Enqueue(() => Check(Native.SetPropertyDouble(_handle, "speed", speed)));
    }

    public void SelectSubtitleTrack(long trackId)
    {
        Enqueue(() => Check(Native.SetPropertyString(_handle, "sid",
            trackId <= 0 ? "no" : trackId.ToString(CultureInfo.InvariantCulture))));
    }

    public void SetQuality(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            throw new ArgumentException("Quality cannot be null or whitespace.", nameof(quality));
        lock (_gate)
        {
            if (IsDisposing || !IsAvailable) return;
            _quality = quality;
            if (!_state.HasMedia || _request is null) return;
            _reload ??= new ReloadSnapshot(_state.PlaylistIndex, _state.Position, _state.IsPaused, _state.Volume,
                _state.Speed);
        }

        Enqueue(LoadCurrentRequest);
    }

    public void UpdatePreferences(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            _preferences = preferences;
        }
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
            Check(Native.Render(renderContext, Native.GetFramebufferBinding(), width, height));
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
            if (_state.HasMedia && _request is not null)
                _reload ??= new ReloadSnapshot(_state.PlaylistIndex, _state.Position, _state.IsPaused, _state.Volume,
                    _state.Speed);
        }

        SetPaused(true);
        Native.SetRenderUpdateCallback(renderContext, 0, 0);
        if (_updateCallbackHandle.IsAllocated) _updateCallbackHandle.Free();
        Native.FreeRenderContext(renderContext);
    }

    public void Stop()
    {
        lock (_seekGate)
        {
            _pendingSeek = null;
        }

        lock (_gate)
        {
            if (IsDisposing || !IsAvailable) return;
            _reload = null;
            _resumeAfterRenderer = false;
            _lastEmittedPosition = TimeSpan.Zero;
            _lastTimePosEmitTimestamp = 0;
            _state = _state with
            {
                PlaylistIndex = -1,
                Position = TimeSpan.Zero,
                Duration = TimeSpan.Zero,
                IsPaused = true,
                IsSeekable = false,
                HasMedia = false,
                IsLoading = false,
                IsBuffering = false,
                BufferingPercent = 0,
                SubtitleTracks = [],
                Chapters = []
            };
        }

        PublishState();
        if (_handle != 0)
            Enqueue(() =>
            {
                Check(Native.Command(_handle, "playlist-play-index", "none"));
                Check(Native.Command(_handle, "playlist-clear"));
                Check(Native.Command(_handle, "stop"));
            });
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
            var mpvEvent = Native.WaitEvent(_handle, -1);
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

    private unsafe void HandleEvent(LibMpvEvent mpvEvent)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch ((LibMpvEventId)mpvEvent.EventId)
        {
            case LibMpvEventId.LogMessage:
                HandleLogMessage(mpvEvent.Data);
                break;
            case LibMpvEventId.PropertyChange:
                HandlePropertyChange(mpvEvent.Data);
                break;
            case LibMpvEventId.FileLoaded:
                HandleFileLoaded();
                LogHardwareDecodingStatus();
                break;
            case LibMpvEventId.VideoReconfig:
                LogHardwareDecodingStatus();
                break;
            case LibMpvEventId.EndFile:
                if (mpvEvent.Data != 0)
                    HandleEndFile(*(LibMpvEventEndFile*)mpvEvent.Data);
                break;
        }
    }

    [GeneratedRegex(@"Waiting\s+(?<seconds>\d+(?:[.,]\d+)?)\s+seconds\s+for\s+the\s+stream\s+to\s+become\s+available",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreamWaitRegex();

    internal static bool TryParseStreamDelay(string prefix, string text, out TimeSpan delay)
    {
        if (string.Equals(prefix, "ytdl_hook", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("stream to become available", StringComparison.OrdinalIgnoreCase))
        {
            var match = StreamWaitRegex().Match(text);
            if (match.Success)
            {
                var secondsSpan = match.Groups["seconds"].ValueSpan;
                Span<char> normalized = stackalloc char[secondsSpan.Length];
                for (var i = 0; i < secondsSpan.Length; i++)
                    normalized[i] = secondsSpan[i] == ',' ? '.' : secondsSpan[i];

                if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                    seconds > 0)
                {
                    delay = TimeSpan.FromSeconds(seconds);
                    return true;
                }
            }
        }

        delay = TimeSpan.Zero;
        return false;
    }

    private unsafe void HandleLogMessage(nint data)
    {
        if (data == 0) return;
        var message = *(LibMpvEventLogMessage*)data;
        var prefix = Marshal.PtrToStringUTF8(message.Prefix) ?? "unknown";
        var text = DiagnosticSanitizer.Sanitize(Marshal.PtrToStringUTF8(message.Text)?.TrimEnd('\r', '\n'));
        if (string.IsNullOrWhiteSpace(text)) return;

        if (TryParseStreamDelay(prefix, text, out var streamDelay))
            Dispatch(() => StreamDelayDetected?.Invoke(this, streamDelay));

        if (message.LogLevel <= LibMpvLogLevel.Error ||
            (message.LogLevel <= LibMpvLogLevel.Warn && (prefix == "ytdl_hook" || text.Contains("ERROR"))))
            _lastErrorMessage = text;
        switch (message.LogLevel)
        {
            case LibMpvLogLevel.Fatal:
            case LibMpvLogLevel.Error:
                Logger.Error("[mpv:{Prefix}] {Message}", prefix, text);
                break;
            case LibMpvLogLevel.Warn:
                Logger.Warning("[mpv:{Prefix}] {Message}", prefix, text);
                break;
            case LibMpvLogLevel.Info:
                Logger.Information("[mpv:{Prefix}] {Message}", prefix, text);
                break;
            default:
                Logger.Debug("[mpv:{Prefix}] {Message}", prefix, text);
                break;
        }
    }

    private void LogHardwareDecodingStatus()
    {
        if (_handle == 0) return;
        var hwdec = Native.GetPropertyString(_handle, "hwdec-current") ?? "no";
        if (string.Equals(hwdec, _lastReportedHwdec, StringComparison.Ordinal)) return;
        _lastReportedHwdec = hwdec;
        Logger.Information("Hardware decoding active: {Hwdec}", hwdec);
    }

    private unsafe void HandlePropertyChange(nint data)
    {
        if (data == 0) return;
        var property = *(LibMpvEventProperty*)data;
        var name = Marshal.PtrToStringUTF8(property.Name);
        switch (name)
        {
            case null:
                return;
            case "hwdec-current":
                LogHardwareDecodingStatus();
                return;
        }

        if (property.Format == LibMpvFormat.None || property.Data == 0)
        {
            if (name != "sid") return;
            lock (_gate)
            {
                _state = _state with
                {
                    SubtitleTracks = SelectSubtitleTrack(_state.SubtitleTracks, 0)
                };
            }

            PublishState();

            return;
        }

        var shouldEmit = true;
        lock (_gate)
        {
            switch (name)
            {
                case "time-pos" when property.Format == LibMpvFormat.Double:
                {
                    var newPosition = ToTimeSpan(*(double*)property.Data);
                    _state = _state with { Position = newPosition };
                    var nowTimestamp = Stopwatch.GetTimestamp();
                    shouldEmit = _lastTimePosEmitTimestamp == 0 ||
                                 Math.Abs((newPosition - _lastEmittedPosition).TotalSeconds) >=
                                 TimePosEmitDeltaSeconds ||
                                 Stopwatch.GetElapsedTime(_lastTimePosEmitTimestamp, nowTimestamp).TotalMilliseconds >=
                                 TimePosEmitIntervalMs;
                    if (shouldEmit)
                    {
                        _lastEmittedPosition = newPosition;
                        _lastTimePosEmitTimestamp = nowTimestamp;
                    }

                    break;
                }
                case "duration" when property.Format == LibMpvFormat.Double:
                    _state = _state with { Duration = ToTimeSpan(*(double*)property.Data) };
                    break;
                case "pause" when property.Format == LibMpvFormat.Flag:
                    _state = _state with { IsPaused = *(int*)property.Data != 0 };
                    break;
                case "mute" when property.Format == LibMpvFormat.Flag:
                    _state = _state with { IsMuted = *(int*)property.Data != 0 };
                    break;
                case "volume" when property.Format == LibMpvFormat.Double:
                    _state = _state with { Volume = *(double*)property.Data };
                    break;
                case "speed" when property.Format == LibMpvFormat.Double:
                    _state = _state with { Speed = *(double*)property.Data };
                    break;
                case "seekable" when property.Format == LibMpvFormat.Flag:
                    _state = _state with { IsSeekable = *(int*)property.Data != 0 };
                    break;
                case "playlist-pos" when property.Format == LibMpvFormat.Int64:
                    _state = _state with { PlaylistIndex = checked((int)*(long*)property.Data) };
                    break;
                case "sid" when property.Format == LibMpvFormat.Int64:
                    _state = _state with
                    {
                        SubtitleTracks = SelectSubtitleTrack(_state.SubtitleTracks, *(long*)property.Data)
                    };
                    break;
                case "paused-for-cache" when property.Format == LibMpvFormat.Flag:
                    _state = _state with { IsBuffering = *(int*)property.Data != 0 };
                    break;
                case "cache-buffering-state" when property.Format == LibMpvFormat.Int64:
                    _state = _state with
                    {
                        BufferingPercent = (int)Math.Clamp(*(long*)property.Data, 0, 100)
                    };
                    break;
                default:
                    return;
            }
            // A reload snapshot is immutable until HandleFileLoaded consumes it.
            // Property notifications from shutdown/reload commands must not replace
            // the state captured before the reload began.
        }

        if (shouldEmit)
            PublishState();
    }

    internal void HandleFileLoaded()
    {
        _lastReportedHwdec = null;
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

            if (_reload is not null &&
                (_reload.PlaylistIndex < 0 || _reload.PlaylistIndex == _state.PlaylistIndex ||
                 _state.PlaylistIndex < 0))
            {
                reload = _reload;
                _reload = null;
            }
            else
            {
                reload = null;
            }
        }

        if (reload is not null)
            Enqueue(() =>
            {
                Check(Native.SetPropertyDouble(_handle, "volume", reload.Volume));
                Check(Native.SetPropertyDouble(_handle, "speed", reload.Speed));
                Check(Native.Command(_handle, "seek",
                    reload.Position.TotalSeconds.ToString(CultureInfo.InvariantCulture), "absolute+exact"));
                Check(Native.SetPropertyFlag(_handle, "pause", reload.IsPaused));
            });

        PublishState();
        LogHardwareDecodingStatus();
    }

    private void HandleEndFile(LibMpvEventEndFile endFile)
    {
        if (endFile.Reason != LibMpvEndFileReason.Error) return;
        var detail = !string.IsNullOrWhiteSpace(_lastErrorMessage)
            ? _lastErrorMessage
            : Native.ErrorString(endFile.Error);
        PublishFailure(detail);
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
        var hasRemoteTargets = urls.Any(static url => !MpvCommandBuilder.IsLocalFileTarget(url));
        if (hasRemoteTargets)
        {
            var rawOptions = BuildYtdlRawOptions(cookieFilePath,
                preferences.MarkWatchedVideos && !preferences.YouTubePlaybackTelemetryEnabled,
                preferences.PreferredSubtitleLanguage);
            Check(Native.SetPropertyString(_handle, "ytdl-raw-options", rawOptions));
            if (!string.IsNullOrWhiteSpace(cookieFilePath))
            {
                Check(Native.SetPropertyFlag(_handle, "cookies", true));
                Check(Native.SetPropertyString(_handle, "cookies-file", cookieFilePath));
            }
            else
            {
                Check(Native.SetPropertyFlag(_handle, "cookies", false));
                Check(Native.SetPropertyString(_handle, "cookies-file", string.Empty));
            }

            Check(Native.SetPropertyString(_handle, "ytdl-format",
                MpvCommandBuilder.BuildYtdlFormat(quality, preferences.PreferredCodec) ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(preferences.YtDlpExecutablePath))
                Check(Native.SetPropertyString(_handle, "script-opts",
                    $"ytdl_hook-ytdl_path={preferences.YtDlpExecutablePath}"));
        }
        else
        {
            Check(Native.SetPropertyString(_handle, "ytdl-raw-options", string.Empty));
            Check(Native.SetPropertyFlag(_handle, "cookies", false));
            Check(Native.SetPropertyString(_handle, "cookies-file", string.Empty));
            Check(Native.SetPropertyString(_handle, "ytdl-format", string.Empty));
        }

        Check(Native.SetPropertyString(_handle, "keep-open",
            preferences.AutoAdvanceNextVideo ? "yes" : "always"));
        Check(Native.SetPropertyFlag(_handle, "pause", false));
        Check(Native.Command(_handle, "loadfile", urls[0], "replace"));
        foreach (var url in urls.Skip(1)) Check(Native.Command(_handle, "loadfile", url, "append-play"));
        if (reload is not null) Check(Native.SetPropertyInt64(_handle, "playlist-pos", reload.PlaylistIndex));
    }

    private List<LibMpvSubtitleTrack> ReadSubtitleTracks()
    {
        var trackListJson = Native.GetPropertyString(_handle, "track-list");
        if (string.IsNullOrWhiteSpace(trackListJson)) return ReadSubtitleTracksLegacy();
        try
        {
            using var doc = JsonDocument.Parse(trackListJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var selectedTrackId = long.TryParse(Native.GetPropertyString(_handle, "sid"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0;

                var subtitleTracks = new List<LibMpvSubtitleTrack>();
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var type = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                    if (!string.Equals(type, "sub", StringComparison.Ordinal) ||
                        !element.TryGetProperty("id", out var idProp) ||
                        !idProp.TryGetInt64(out var trackId))
                        continue;

                    var title = element.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString()
                        : element.TryGetProperty("metadata", out var meta) &&
                          meta.TryGetProperty("title", out var metaTitle)
                            ? metaTitle.GetString()
                            : null;

                    var language = element.TryGetProperty("lang", out var langProp)
                        ? langProp.GetString()
                        : element.TryGetProperty("metadata", out var meta2) &&
                          meta2.TryGetProperty("language", out var metaLang)
                            ? metaLang.GetString()
                            : null;

                    var isSelected = (element.TryGetProperty("selected", out var selProp) &&
                                      (selProp.ValueKind == JsonValueKind.True ||
                                       (selProp.ValueKind == JsonValueKind.String && string.Equals(selProp.GetString(),
                                           "yes", StringComparison.OrdinalIgnoreCase)))) ||
                                     trackId == selectedTrackId;

                    var label = string.IsNullOrWhiteSpace(title)
                        ? language
                        : string.IsNullOrWhiteSpace(language) ||
                          string.Equals(title, language, StringComparison.OrdinalIgnoreCase)
                            ? title
                            : $"{title} ({language})";

                    subtitleTracks.Add(new LibMpvSubtitleTrack(trackId, language ?? label ?? $"Subtitle {trackId}",
                        label ?? $"Subtitle {trackId}", isSelected));
                }

                return subtitleTracks;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to parse subtitle track-list JSON from libmpv");
        }

        return ReadSubtitleTracksLegacy();
    }

    private List<LibMpvSubtitleTrack> ReadSubtitleTracksLegacy()
    {
        if (!int.TryParse(Native.GetPropertyString(_handle, "track-list/count"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackCount) ||
            trackCount <= 0)
            return [];

        var selectedTrackId = long.TryParse(Native.GetPropertyString(_handle, "sid"),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
        var subtitleTracks = new List<LibMpvSubtitleTrack>();
        for (var index = 0; index < trackCount; index++)
        {
            var propertyPrefix = $"track-list/{index}";
            if (!string.Equals(Native.GetPropertyString(_handle, $"{propertyPrefix}/type"), "sub",
                    StringComparison.Ordinal) ||
                !long.TryParse(Native.GetPropertyString(_handle, $"{propertyPrefix}/id"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackId))
                continue;

            var title = Native.GetPropertyString(_handle, $"{propertyPrefix}/title");
            var language = Native.GetPropertyString(_handle, $"{propertyPrefix}/lang");
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
        if (!int.TryParse(Native.GetPropertyString(_handle, "chapter-list/count"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapterCount) ||
            chapterCount <= 0)
            return [];

        var chapters = new List<LibMpvChapter>(chapterCount);
        for (var index = 0; index < chapterCount; index++)
        {
            var propertyPrefix = $"chapter-list/{index}";
            if (!double.TryParse(Native.GetPropertyString(_handle, $"{propertyPrefix}/time"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                continue;

            var title = Native.GetPropertyString(_handle, $"{propertyPrefix}/title");
            chapters.Add(new LibMpvChapter(TimeSpan.FromSeconds(seconds),
                string.IsNullOrWhiteSpace(title) ? $"Chapter {index + 1}" : title));
        }

        return chapters;
    }

    public PlaybackStats? GetPlaybackStats(bool includeTracks = true)
    {
        EnsureInitialized();
        if (!IsAvailable || IsDisposing || _handle == 0) return null;
        lock (_gate)
        {
            if (_handle == 0 || !_state.HasMedia) return null;
        }

        try
        {
            // 1. File / Stream
            var mediaTitle = Native.GetPropertyString(_handle, "media-title") ??
                             _request?.Videos.FirstOrDefault()?.Title;
            var fileFormat = Native.GetPropertyString(_handle, "file-format");
            var demuxer = Native.GetPropertyString(_handle, "demuxer");
            var path = Native.GetPropertyString(_handle, "path") ??
                       Native.GetPropertyString(_handle, "stream-open-filename");
            var fileSizeStr = Native.GetPropertyString(_handle, "file-size");
            long? fileSize = long.TryParse(fileSizeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fs)
                ? fs
                : null;

            // 2. Video
            var videoCodec = Native.GetPropertyString(_handle, "video-codec") ??
                             Native.GetPropertyString(_handle, "video-format");
            var videoDecoder = Native.GetPropertyString(_handle, "video-decoder-name") ??
                               Native.GetPropertyString(_handle, "video-codec");
            var hwdec = Native.GetPropertyString(_handle, "hwdec-current") ??
                        Native.GetPropertyString(_handle, "hwdec-active");

            var vwStr = Native.GetPropertyString(_handle, "video-params/w");
            var vhStr = Native.GetPropertyString(_handle, "video-params/h");
            int? videoWidth = int.TryParse(vwStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vw)
                ? vw
                : null;
            int? videoHeight = int.TryParse(vhStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vh)
                ? vh
                : null;

            var dwStr = Native.GetPropertyString(_handle, "dwidth") ??
                        Native.GetPropertyString(_handle, "video-params/dw");
            var dhStr = Native.GetPropertyString(_handle, "dheight") ??
                        Native.GetPropertyString(_handle, "video-params/dh");
            int? displayWidth = int.TryParse(dwStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dw)
                ? dw
                : null;
            int? displayHeight = int.TryParse(dhStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dh)
                ? dh
                : null;

            var aspectStr = Native.GetPropertyString(_handle, "video-params/aspect");
            double? aspect =
                double.TryParse(aspectStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var asp) &&
                !double.IsNaN(asp)
                    ? asp
                    : null;

            var contFpsStr = Native.GetPropertyString(_handle, "container-fps");
            double? containerFps =
                double.TryParse(contFpsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var cfps) &&
                !double.IsNaN(cfps)
                    ? cfps
                    : null;

            var estFpsStr = Native.GetPropertyString(_handle, "estimated-vf-fps");
            double? estimatedFps =
                double.TryParse(estFpsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var efps) &&
                !double.IsNaN(efps)
                    ? efps
                    : null;

            var vBitrateStr = Native.GetPropertyString(_handle, "video-bitrate");
            double? videoBitrate =
                double.TryParse(vBitrateStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var vbr) &&
                !double.IsNaN(vbr)
                    ? vbr
                    : null;

            var pixelFormat = Native.GetPropertyString(_handle, "video-params/pixelformat");
            var colorMatrix = Native.GetPropertyString(_handle, "video-params/colormatrix");
            var colorLevels = Native.GetPropertyString(_handle, "video-params/colorlevels");
            var primaries = Native.GetPropertyString(_handle, "video-params/primaries");
            var gamma = Native.GetPropertyString(_handle, "video-params/gamma");

            // 3. Audio
            var audioCodec = Native.GetPropertyString(_handle, "audio-codec-name") ??
                             Native.GetPropertyString(_handle, "audio-codec") ??
                             Native.GetPropertyString(_handle, "audio-format");
            var audioDecoder = Native.GetPropertyString(_handle, "audio-decoder-name") ?? audioCodec;

            var sampleRateStr = Native.GetPropertyString(_handle, "audio-params/samplerate");
            int? sampleRate =
                int.TryParse(sampleRateStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sr) ? sr : null;

            var channelCountStr = Native.GetPropertyString(_handle, "audio-params/channel-count");
            int? channelCount =
                int.TryParse(channelCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cc)
                    ? cc
                    : null;

            var channels = Native.GetPropertyString(_handle, "audio-params/channels");
            var audioFormat = Native.GetPropertyString(_handle, "audio-params/format");

            var aBitrateStr = Native.GetPropertyString(_handle, "audio-bitrate");
            double? audioBitrate =
                double.TryParse(aBitrateStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var abr) &&
                !double.IsNaN(abr)
                    ? abr
                    : null;

            // 4. Performance & Sync
            var dropCountStr = Native.GetPropertyString(_handle, "frame-drop-count") ??
                               Native.GetPropertyString(_handle, "decoder-frame-drop-count");
            long? dropCount =
                long.TryParse(dropCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc) ? dc : null;

            var voDropCountStr = Native.GetPropertyString(_handle, "vo-drop-frame-count");
            long? voDropCount =
                long.TryParse(voDropCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vdc)
                    ? vdc
                    : null;

            var mistimedStr = Native.GetPropertyString(_handle, "mistimed-frame-count");
            long? mistimed = long.TryParse(mistimedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mt)
                ? mt
                : null;

            var vsyncRatioStr = Native.GetPropertyString(_handle, "vsync-ratio");
            double? vsyncRatio =
                double.TryParse(vsyncRatioStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var vr) &&
                !double.IsNaN(vr)
                    ? vr
                    : null;

            var avsyncStr = Native.GetPropertyString(_handle, "avsync");
            double? avsync =
                double.TryParse(avsyncStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var avs) &&
                !double.IsNaN(avs)
                    ? avs
                    : null;

            var cacheDurStr = Native.GetPropertyString(_handle, "demuxer-cache-duration");
            double? cacheDuration =
                double.TryParse(cacheDurStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var cd) &&
                !double.IsNaN(cd)
                    ? cd
                    : null;

            var cacheBytesStr = Native.GetPropertyString(_handle, "demuxer-cache-state/bytes");
            long? cacheBytes =
                long.TryParse(cacheBytesStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cb)
                    ? cb
                    : null;

            // 5. Playback
            var percentStr = Native.GetPropertyString(_handle, "percent-pos");
            double? percent =
                double.TryParse(percentStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pp) &&
                !double.IsNaN(pp)
                    ? pp
                    : null;

            LibMpvPlaybackState stateSnapshot;
            lock (_gate)
            {
                stateSnapshot = _state;
            }

            var selectedSub = stateSnapshot.SubtitleTracks.FirstOrDefault(t => t.IsSelected)?.Label;
            var selectedAudio = Native.GetPropertyString(_handle, "current-tracks/audio/title") ??
                                Native.GetPropertyString(_handle, "current-tracks/audio/lang");

            // 6. Tracks
            var tracks = includeTracks ? ReadAllTracks() : [];

            // 7. Engine
            var mpvVersion = _mpvVersion ??= Native.GetPropertyString(_handle, "mpv-version");
            var ffmpegVersion = _ffmpegVersion ??= Native.GetPropertyString(_handle, "ffmpeg-version");

            return new PlaybackStats(
                mediaTitle,
                fileFormat,
                demuxer,
                path,
                fileSize,
                videoCodec,
                videoDecoder,
                hwdec,
                videoWidth,
                videoHeight,
                displayWidth,
                displayHeight,
                aspect,
                containerFps,
                estimatedFps,
                videoBitrate,
                pixelFormat,
                colorMatrix,
                colorLevels,
                primaries,
                gamma,
                audioCodec,
                audioDecoder,
                sampleRate,
                channelCount,
                channels,
                audioFormat,
                audioBitrate,
                dropCount,
                voDropCount,
                mistimed,
                vsyncRatio,
                avsync,
                cacheDuration,
                cacheBytes,
                stateSnapshot.Position,
                stateSnapshot.Duration,
                percent,
                stateSnapshot.Speed,
                stateSnapshot.Volume,
                stateSnapshot.IsMuted,
                stateSnapshot.IsPaused,
                selectedSub,
                selectedAudio,
                tracks,
                mpvVersion,
                ffmpegVersion,
                "libmpv (OpenGL)");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to query playback stats from libmpv");
            return null;
        }
    }

    private List<PlaybackStatsTrack> ReadAllTracks()
    {
        var trackListJson = Native.GetPropertyString(_handle, "track-list");
        if (string.IsNullOrWhiteSpace(trackListJson)) return ReadAllTracksLegacy();
        try
        {
            using var doc = JsonDocument.Parse(trackListJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var tracks = new List<PlaybackStatsTrack>(doc.RootElement.GetArrayLength());
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var id))
                        continue;

                    var type = element.TryGetProperty("type", out var typeProp)
                        ? typeProp.GetString() ?? "unknown"
                        : "unknown";

                    var title = element.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString()
                        : element.TryGetProperty("metadata", out var meta) &&
                          meta.TryGetProperty("title", out var metaTitle)
                            ? metaTitle.GetString()
                            : null;

                    var lang = element.TryGetProperty("lang", out var langProp)
                        ? langProp.GetString()
                        : element.TryGetProperty("metadata", out var meta2) &&
                          meta2.TryGetProperty("language", out var metaLang)
                            ? metaLang.GetString()
                            : null;

                    var codec = element.TryGetProperty("codec", out var codecProp) ? codecProp.GetString() : null;

                    int? w = element.TryGetProperty("demux-w", out var wProp) && wProp.TryGetInt32(out var tw)
                        ? tw
                        : null;
                    int? h = element.TryGetProperty("demux-h", out var hProp) && hProp.TryGetInt32(out var th)
                        ? th
                        : null;
                    double? fps = element.TryGetProperty("demux-fps", out var fpsProp) &&
                                  fpsProp.TryGetDouble(out var tfps)
                        ? tfps
                        : null;
                    int? sr = element.TryGetProperty("demux-samplerate", out var srProp) &&
                              srProp.TryGetInt32(out var tsr)
                        ? tsr
                        : null;
                    var ch = element.TryGetProperty("demux-channels", out var chProp) ? chProp.GetString() : null;
                    double? br = element.TryGetProperty("demux-bitrate", out var brProp) &&
                                 brProp.TryGetDouble(out var tbr)
                        ? tbr
                        : null;

                    var isSelected = element.TryGetProperty("selected", out var selProp) &&
                                     (selProp.ValueKind == JsonValueKind.True ||
                                      (selProp.ValueKind == JsonValueKind.String && string.Equals(selProp.GetString(),
                                          "yes", StringComparison.OrdinalIgnoreCase)));

                    var isDefault = element.TryGetProperty("default", out var defProp) &&
                                    (defProp.ValueKind == JsonValueKind.True ||
                                     (defProp.ValueKind == JsonValueKind.String && string.Equals(defProp.GetString(),
                                         "yes", StringComparison.OrdinalIgnoreCase)));

                    tracks.Add(new PlaybackStatsTrack(id, type, title, lang, codec, w, h, fps, sr, ch, br,
                        isSelected, isDefault));
                }

                return tracks;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to parse track-list JSON from libmpv");
        }

        return ReadAllTracksLegacy();
    }

    private List<PlaybackStatsTrack> ReadAllTracksLegacy()
    {
        if (!int.TryParse(Native.GetPropertyString(_handle, "track-list/count"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var trackCount) ||
            trackCount <= 0)
            return [];

        var tracks = new List<PlaybackStatsTrack>(trackCount);
        for (var i = 0; i < trackCount; i++)
        {
            var prefix = $"track-list/{i}";
            var type = Native.GetPropertyString(_handle, $"{prefix}/type") ?? "unknown";
            if (!long.TryParse(Native.GetPropertyString(_handle, $"{prefix}/id"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var id))
                continue;

            var title = Native.GetPropertyString(_handle, $"{prefix}/title");
            var lang = Native.GetPropertyString(_handle, $"{prefix}/lang");
            var codec = Native.GetPropertyString(_handle, $"{prefix}/codec");

            int? w = int.TryParse(Native.GetPropertyString(_handle, $"{prefix}/demux-w"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var tw)
                ? tw
                : null;
            int? h = int.TryParse(Native.GetPropertyString(_handle, $"{prefix}/demux-h"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var th)
                ? th
                : null;
            double? fps = double.TryParse(Native.GetPropertyString(_handle, $"{prefix}/demux-fps"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var tfps)
                ? tfps
                : null;
            int? sr = int.TryParse(Native.GetPropertyString(_handle, $"{prefix}/demux-samplerate"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var tsr)
                ? tsr
                : null;
            var ch = Native.GetPropertyString(_handle, $"{prefix}/demux-channels");
            double? br = double.TryParse(Native.GetPropertyString(_handle, $"{prefix}/demux-bitrate"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var tbr)
                ? tbr
                : null;

            var isSelected = string.Equals(Native.GetPropertyString(_handle, $"{prefix}/selected"), "yes",
                StringComparison.OrdinalIgnoreCase);
            var isDefault = string.Equals(Native.GetPropertyString(_handle, $"{prefix}/default"), "yes",
                StringComparison.OrdinalIgnoreCase);

            tracks.Add(new PlaybackStatsTrack(id, type, title, lang, codec, w, h, fps, sr, ch, br, isSelected,
                isDefault));
        }

        return tracks;
    }

    private static LibMpvSubtitleTrack[] SelectSubtitleTrack(
        IReadOnlyList<LibMpvSubtitleTrack> tracks, long selectedTrackId)
    {
        return [.. tracks.Select(track => track with { IsSelected = track.Id == selectedTrackId })];
    }

    private static string BuildYtdlRawOptions(
        string? cookieFilePath,
        bool markWatchedVideos,
        string? preferredSubtitleLanguage = null)
    {
        var options = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(cookieFilePath))
            options.Add($"cookies={cookieFilePath}");

        var sanitizedSubLang = SanitizeSubtitleLanguage(preferredSubtitleLanguage);
        if (sanitizedSubLang is not null)
        {
            options.Add("write-subs=");
            options.Add(string.Equals(sanitizedSubLang, "all", StringComparison.OrdinalIgnoreCase)
                ? "sub-langs=all"
                : $"sub-langs={sanitizedSubLang}.*");
            options.Add("sub-format=vtt");
        }

        if (markWatchedVideos)
            options.Add("mark-watched=");

        return options.Count == 0 ? string.Empty : string.Join(',', options);
    }

    private static string? SanitizeSubtitleLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var trimmed = language.AsSpan().Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                sb.Append(ch);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private void Observe(string name, LibMpvFormat format)
    {
        Check(Native.ObserveProperty(_handle, 0, name, format));
    }

    private void Enqueue(Action action)
    {
        EnsureInitialized();
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
        var sanitized = DiagnosticSanitizer.Sanitize(detail);
        Logger.Error("LibMpv playback failure: {Detail}", sanitized);
        Dispatch(() => PlaybackFailed?.Invoke(this, sanitized));
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
        if (result < 0) throw new LibMpvException(Native.ErrorString(result));
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