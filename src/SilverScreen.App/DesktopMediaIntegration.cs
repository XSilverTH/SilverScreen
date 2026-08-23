using System.Text;
using GLib;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.DBus;
using SilverScreen.Infrastructure;
using SilverScreen.Infrastructure.Features.Playback;
using Tmds.DBus.Protocol;
using TimeSpan = System.TimeSpan;

namespace SilverScreen;

/// <summary>Publishes embedded playback to desktop media controls and keeps the display awake while it is playing.</summary>
internal sealed class DesktopMediaIntegration : IDisposable
{
    private const string MprisObjectPath = "/org/mpris/MediaPlayer2";
    private const string MprisServiceName = "org.mpris.MediaPlayer2.SilverScreen";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string PortalServiceName = "org.freedesktop.portal.Desktop";
    private const uint PortalIdleInhibitFlag = 4;

    private static readonly ILogger Logger = Log.ForContext<DesktopMediaIntegration>();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _inhibitGate = new(1, 1);
    private readonly LibMpvPlayer _player;
    private readonly Action _raiseRequested;
    private DBusConnection? _connection;
    private bool _disposed;
    private MprisHandler? _mprisHandler;
    private bool _portalAvailable = true;
    private ObjectPath? _portalInhibitRequest;
    private DesktopPlaybackSnapshot _snapshot = DesktopPlaybackSnapshot.Stopped;

    public DesktopMediaIntegration(LibMpvPlayer player, Action raiseRequested)
    {
        _player = player;
        _raiseRequested = raiseRequested;
        ConnectAsync().FireAndForget(Logger);
    }

    public void Dispose()
    {
        DBusConnection? connection;
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            connection = _connection;
            _connection = null;
            _mprisHandler?.Dispose();
            _mprisHandler = null;
        }

        try
        {
            connection?.Dispose();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to close the desktop D-Bus connection.");
        }
    }

    public void UpdatePlayback(PlaybackRequest? request, LibMpvPlaybackState state)
    {
        DesktopPlaybackSnapshot previous;
        DesktopPlaybackSnapshot current;
        MprisHandler? handler;
        lock (_gate)
        {
            if (_disposed) return;

            previous = _snapshot;
            current = DesktopPlaybackSnapshot.Create(request, state);
            _snapshot = current;
            handler = _mprisHandler;
        }

        handler?.PublishChanges(previous, current);
        UpdateInhibition();
    }

    public void ClearPlayback()
    {
        UpdatePlayback(null, new LibMpvPlaybackState(-1, TimeSpan.Zero, TimeSpan.Zero, true, false, 100, 1,
            false, false, false, [], []));
    }

    private async Task ConnectAsync()
    {
        var address = DBusAddress.Session;
        if (address is null)
        {
            Logger.Information("Desktop media integration is unavailable because there is no D-Bus session bus.");
            return;
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);

            var handler = new MprisHandler(this, connection);
            connection.AddMethodHandler(handler);
            await connection.RequestNameAsync(MprisServiceName).ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed)
                {
                    handler.Dispose();
                    return;
                }

                _connection = connection;
                _mprisHandler = handler;
            }

            handler.PublishInitialState();
            await UpdateInhibitionAsync();

            var disconnectReason = await connection.DisconnectedAsync().ConfigureAwait(false);
            if (disconnectReason is not null)
                Logger.Warning(disconnectReason, "Desktop media D-Bus connection was disconnected.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Desktop media integration is unavailable; continuing without D-Bus support.");
        }
        finally
        {
            lock (_gate)
            {
                _connection = null;
                _mprisHandler?.Dispose();
                _mprisHandler = null;
                _portalInhibitRequest = null;
            }
        }
    }

    private DesktopPlaybackSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    private void Raise()
    {
        Functions.IdleAdd(0, () =>
        {
            lock (_gate)
            {
                if (_disposed) return false;
            }

            _raiseRequested();
            return false;
        });
    }

    private void UpdateInhibition()
    {
        UpdateInhibitionAsync().FireAndForget(Logger);
    }

    private async Task UpdateInhibitionAsync()
    {
        try
        {
            await _inhibitGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            DBusConnection? connection;
            ObjectPath? request;
            bool shouldInhibit;
            lock (_gate)
            {
                if (_disposed || !_portalAvailable) return;

                connection = _connection;
                request = _portalInhibitRequest;
                shouldInhibit = _snapshot.IsPlaying;
            }

            if (connection is null || shouldInhibit == request is not null) return;

            if (shouldInhibit)
            {
                try
                {
                    var portal = new DBusService(connection, PortalServiceName).CreateInhibit(PortalObjectPath);
                    var handle = await portal.InhibitAsync(string.Empty, PortalIdleInhibitFlag,
                        new Dictionary<string, VariantValue> { ["reason"] = "Playing video" }).ConfigureAwait(false);
                    var releaseImmediately = false;
                    lock (_gate)
                    {
                        if (_disposed || _connection != connection || !_snapshot.IsPlaying)
                            releaseImmediately = true;
                        else
                            _portalInhibitRequest = handle;
                    }

                    if (releaseImmediately)
                        await ClosePortalRequestAsync(connection, handle).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        _portalAvailable = false;
                    }

                    Logger.Warning(exception, "Could not inhibit desktop idle lock through the portal.");
                }
            }
            else if (request is { } handle)
            {
                lock (_gate)
                {
                    _portalInhibitRequest = null;
                }

                await ClosePortalRequestAsync(connection, handle).ConfigureAwait(false);
            }
        }
        finally
        {
            _inhibitGate.Release();
        }
    }

    private static async Task ClosePortalRequestAsync(DBusConnection connection, ObjectPath request)
    {
        try
        {
            var portalRequest = new DBusService(connection, PortalServiceName).CreateRequest(request);
            await portalRequest.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not release the portal idle-inhibit request.");
        }
    }

    private sealed class MprisHandler(DesktopMediaIntegration owner, DBusConnection connection) : DBusHandler(
            connection,
            MprisObjectPath, false),
        IMediaPlayer2Handler, IMediaPlayer2Properties,
        IPlayerHandler, IPlayerProperties, IDisposable
    {
        public void Dispose()
        {
        }

        ValueTask IMediaPlayer2Handler.RaiseAsync()
        {
            owner.Raise();
            return default;
        }

        ValueTask IMediaPlayer2Handler.QuitAsync()
        {
            return default;
        }

        ValueTask IMediaPlayer2Handler.HandleGetPropertyAsync(IMediaPlayer2Handler.GetPropertyContext context)
        {
            return context.Handle(this);
        }

        ValueTask IMediaPlayer2Handler.HandleGetAllPropertiesAsync(IMediaPlayer2Handler.GetAllPropertiesContext context)
        {
            return context.Handle(this);
        }

        bool IMediaPlayer2Properties.CanQuit => false;
        bool IMediaPlayer2Properties.CanRaise => true;
        bool IMediaPlayer2Properties.HasTrackList => false;
        string IMediaPlayer2Properties.Identity => ApplicationMetadata.ApplicationName;
        string IMediaPlayer2Properties.DesktopEntry => ApplicationMetadata.ApplicationId;
        string[] IMediaPlayer2Properties.SupportedUriSchemes => ["http", "https"];
        string[] IMediaPlayer2Properties.SupportedMimeTypes => ["video/mp4", "video/webm"];

        ValueTask IPlayerHandler.NextAsync()
        {
            owner._player.MovePlaylist(true);
            return default;
        }

        ValueTask IPlayerHandler.PreviousAsync()
        {
            owner._player.MovePlaylist(false);
            return default;
        }

        ValueTask IPlayerHandler.PauseAsync()
        {
            owner._player.SetPaused(true);
            return default;
        }

        ValueTask IPlayerHandler.PlayPauseAsync()
        {
            owner._player.TogglePause();
            return default;
        }

        ValueTask IPlayerHandler.StopAsync()
        {
            owner._player.Stop();
            return default;
        }

        ValueTask IPlayerHandler.PlayAsync()
        {
            owner._player.SetPaused(false);
            return default;
        }

        ValueTask IPlayerHandler.SeekAsync(long offset)
        {
            owner._player.SeekRelative(offset / 1_000_000d);
            return default;
        }

        ValueTask IPlayerHandler.SetPositionAsync(ObjectPath trackId, long position)
        {
            var snapshot = owner.GetSnapshot();
            if (trackId == snapshot.TrackId)
                owner._player.SeekAbsolute(Math.Max(0, position) / 1_000_000d);
            return default;
        }


        ValueTask IPlayerHandler.HandleGetPropertyAsync(IPlayerHandler.GetPropertyContext context)
        {
            return context.Handle(this);
        }

        ValueTask IPlayerHandler.HandleGetAllPropertiesAsync(IPlayerHandler.GetAllPropertiesContext context)
        {
            return context.Handle(this);
        }

        ValueTask IPlayerHandler.HandleSetPropertyAsync(IPlayerHandler.SetPropertyContext context)
        {
            return context.Handle(this);
        }

        string IPlayerProperties.PlaybackStatus => owner.GetSnapshot().PlaybackStatus;
        string IPlayerProperties.LoopStatus => "None";

        double IPlayerProperties.Rate
        {
            get => owner.GetSnapshot().Rate;
            set => owner._player.SetSpeed(Math.Clamp(value, 0.25, 4));
        }

        bool IPlayerProperties.Shuffle => false;
        Dictionary<string, VariantValue> IPlayerProperties.Metadata => owner.GetSnapshot().Metadata;

        double IPlayerProperties.Volume
        {
            get => owner.GetSnapshot().Volume;
            set => owner._player.SetVolume(Math.Clamp(value, 0, 1) * 100);
        }

        long IPlayerProperties.Position => owner.GetSnapshot().PositionMicroseconds;
        double IPlayerProperties.MinimumRate => 0.25;
        double IPlayerProperties.MaximumRate => 4;
        bool IPlayerProperties.CanGoNext => owner.GetSnapshot().CanGoNext;
        bool IPlayerProperties.CanGoPrevious => owner.GetSnapshot().CanGoPrevious;
        bool IPlayerProperties.CanPlay => owner.GetSnapshot().CanPlay;
        bool IPlayerProperties.CanPause => owner.GetSnapshot().CanPause;
        bool IPlayerProperties.CanSeek => owner.GetSnapshot().CanSeek;
        bool IPlayerProperties.CanControl => true;

        public void PublishInitialState()
        {
            var snapshot = owner.GetSnapshot();
            PublishChanges(DesktopPlaybackSnapshot.Stopped, snapshot);
        }

        public void PublishChanges(DesktopPlaybackSnapshot previous, DesktopPlaybackSnapshot current)
        {
            try
            {
                if (!Equals(previous.TrackId, current.TrackId))
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.Metadata);
                if (previous.PlaybackStatus != current.PlaybackStatus)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.PlaybackStatus);
                if (Math.Abs(previous.Volume - current.Volume) > 0.1)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.Volume);
                if (Math.Abs(previous.Rate - current.Rate) > 0.1)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.Rate);
                if (previous.CanSeek != current.CanSeek)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.CanSeek);
                if (previous.CanGoNext != current.CanGoNext)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.CanGoNext);
                if (previous.CanGoPrevious != current.CanGoPrevious)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.CanGoPrevious);
                if (previous.CanPlay != current.CanPlay)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.CanPlay);
                if (previous.CanPause != current.CanPause)
                    Connection.EmitPropertyChanged(MprisObjectPath, this, PlayerProperty.CanPause);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to publish an MPRIS state update.");
            }
        }
    }

    internal sealed record DesktopPlaybackSnapshot(
        bool HasMedia,
        bool IsPlaying,
        string PlaybackStatus,
        long PositionMicroseconds,
        double Volume,
        double Rate,
        bool CanSeek,
        bool CanGoNext,
        bool CanGoPrevious,
        bool CanPlay,
        bool CanPause,
        ObjectPath TrackId,
        Dictionary<string, VariantValue> Metadata)
    {
        public static DesktopPlaybackSnapshot Stopped { get; } = new(false, false, "Stopped", 0, 1, 1, false,
            false, false, false, false, new ObjectPath("/org/mpris/MediaPlayer2/Track/none"), []);

        public static DesktopPlaybackSnapshot Create(PlaybackRequest? request, LibMpvPlaybackState state)
        {
            if (!state.HasMedia || request is null || state.PlaylistIndex < 0 ||
                state.PlaylistIndex >= request.Videos.Length)
                return Stopped with { Volume = Math.Clamp(state.Volume / 100, 0, 1), Rate = state.Speed };

            var video = request.Videos[state.PlaylistIndex];
            var trackId =
                new ObjectPath(
                    $"/org/mpris/MediaPlayer2/Track/{Convert.ToHexString(Encoding.UTF8.GetBytes(video.Id))}");
            var duration = state.Duration == TimeSpan.Zero ? video.Duration : state.Duration;
            var metadata = new Dictionary<string, VariantValue>
            {
                ["mpris:trackid"] = trackId,
                ["xesam:title"] = video.Title,
                ["xesam:artist"] = VariantValue.Array(new[] { video.ChannelName }),
                ["mpris:artUrl"] = video.ThumbnailUrl,
                ["xesam:url"] = video.WatchUrl ?? PlaybackRequest.BuildWatchUrl(video.Id) ?? string.Empty
            };
            if (duration > TimeSpan.Zero)
                metadata["mpris:length"] = duration.Ticks / 10;

            return new DesktopPlaybackSnapshot(true, !state.IsPaused, state.IsPaused ? "Paused" : "Playing",
                state.Position.Ticks / 10, Math.Clamp(state.Volume / 100, 0, 1), state.Speed, state.IsSeekable,
                state.PlaylistIndex < request.Videos.Length - 1, state.PlaylistIndex > 0, true, true, trackId,
                metadata);
        }
    }
}