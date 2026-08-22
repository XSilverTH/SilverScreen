using DiscordRPC;
using DiscordRPC.Entities;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

internal interface IDiscordRpcClient : IDisposable
{
    event EventHandler? Ready;
    event EventHandler? ConnectionFailed;
    bool Initialize();
    void SetPresence(RichPresence presence);
    void ClearPresence();
}

internal sealed class DiscordRpcClientAdapter : IDiscordRpcClient
{
    private readonly DiscordRpcClient _client;

    public DiscordRpcClientAdapter(string applicationId)
    {
        _client = new DiscordRpcClient(applicationId);
        _client.OnReady += (_, _) => Ready?.Invoke(this, EventArgs.Empty);
        _client.OnConnectionFailed += (_, _) => ConnectionFailed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Ready;
    public event EventHandler? ConnectionFailed;

    public bool Initialize()
    {
        return _client.Initialize();
    }

    public void SetPresence(RichPresence presence)
    {
        _client.SetPresence(presence);
    }

    public void ClearPresence()
    {
        _client.ClearPresence();
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

public sealed class DiscordPresenceService : IPlaybackPresenceService
{
    private static readonly ILogger Logger = Log.ForContext<DiscordPresenceService>();
    private static readonly TimeSpan ConnectionReadyTimeout = TimeSpan.FromSeconds(20);
    private readonly string? _applicationId;
    private readonly Func<string, IDiscordRpcClient> _clientFactory;
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private CachedActivity? _cachedActivity;

    private IDiscordRpcClient? _client;
    private bool _clientReady;
    private bool _disposed;
    private bool _enabled;
    private CachedActivity? _lastPublishedActivity;

    public DiscordPresenceService(IPreferencesService preferencesService, string? applicationId)
        : this(preferencesService, applicationId, static id => new DiscordRpcClientAdapter(id))
    {
    }

    internal DiscordPresenceService(
        IPreferencesService preferencesService,
        string? applicationId,
        Func<string, IDiscordRpcClient> clientFactory)
    {
        _preferencesService = preferencesService;
        _applicationId = applicationId;
        _clientFactory = clientFactory;

        _preferencesService.PreferencesChanged += OnPreferencesChanged;
        ApplyEnabledState(_preferencesService.GetPreferences().DiscordRichPresenceEnabled);
    }

    public void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);

        lock (_lock)
        {
            if (_disposed) return;

            _cachedActivity = new CachedActivity(request, state);
            if (!_enabled) return;

            EnsureClientLocked();
            PublishCachedActivityLocked();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed) return;

            _cachedActivity = null;
            _lastPublishedActivity = null;
            ClearPresenceLocked();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;

            _disposed = true;
            _preferencesService.PreferencesChanged -= OnPreferencesChanged;
            _cachedActivity = null;
            _lastPublishedActivity = null;
            ClearAndDisposeClientLocked();
        }
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        ApplyEnabledState(preferences.DiscordRichPresenceEnabled);
    }

    private void ApplyEnabledState(bool enabled)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _enabled = enabled;
            if (!enabled)
            {
                ClearAndDisposeClientLocked();
                return;
            }

            if (_cachedActivity is null) return;
            EnsureClientLocked();
            PublishCachedActivityLocked();
        }
    }

    private void EnsureClientLocked()
    {
        if (_client is not null) return;

        if (!ulong.TryParse(_applicationId, out _))
        {
            Logger.Warning(
                "Discord Rich Presence is enabled but SILVERSCREEN_DISCORD_APPLICATION_ID is missing or invalid.");
            return;
        }

        IDiscordRpcClient? client;
        try
        {
            client = _clientFactory(_applicationId!);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not create RPC client");
            return;
        }

        SubscribeClientEvents(client);
        _client = client;
        _clientReady = false;
        try
        {
            if (!client.Initialize())
            {
                _client = null;
                UnsubscribeClientEvents(client);
                DisposeClientQuietly(client);
            }
        }
        catch (Exception ex)
        {
            _client = null;
            UnsubscribeClientEvents(client);
            Logger.Warning(ex, "Could not initialize RPC client");
            DisposeClientQuietly(client);
            _clientReady = false;
        }

        if (_client is not null) ScheduleConnectionTimeout(_client);
    }

    private void OnClientReady(object? sender, EventArgs args)
    {
        lock (_lock)
        {
            if (_disposed || !ReferenceEquals(sender, _client)) return;
            _clientReady = true;
            PublishCachedActivityLocked(true);
        }
    }

    private void OnClientConnectionFailed(object? sender, EventArgs args)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(sender, _client)) return;

            var client = _client!;
            _clientReady = false;
            _client = null;
            _lastPublishedActivity = null;
            UnsubscribeClientEvents(client);
            Logger.Debug("Discord Rich Presence connection failed; retrying on the next playback state update");
            DisposeClientQuietly(client);
        }
    }

    private void ScheduleConnectionTimeout(IDiscordRpcClient client)
    {
        RestartStaleConnectionAsync(client).FireAndForget(Logger);
    }

    private async Task RestartStaleConnectionAsync(IDiscordRpcClient client)
    {
        await Task.Delay(ConnectionReadyTimeout).ConfigureAwait(false);

        lock (_lock)
        {
            if (_disposed || !_enabled || _cachedActivity is null || _clientReady ||
                !ReferenceEquals(client, _client)) return;

            _client = null;
            _lastPublishedActivity = null;
            UnsubscribeClientEvents(client);
            Logger.Debug("Discord Rich Presence connection did not become ready; reconnecting");
            DisposeClientQuietly(client);
            EnsureClientLocked();
            PublishCachedActivityLocked();
        }
    }

    private void SubscribeClientEvents(IDiscordRpcClient client)
    {
        client.Ready += OnClientReady;
        client.ConnectionFailed += OnClientConnectionFailed;
    }

    private void UnsubscribeClientEvents(IDiscordRpcClient client)
    {
        client.Ready -= OnClientReady;
        client.ConnectionFailed -= OnClientConnectionFailed;
    }

    private void PublishCachedActivityLocked(bool force = false)
    {
        if (_client is null || _cachedActivity is null ||
            (!force && !ShouldPublish(_lastPublishedActivity, _cachedActivity))) return;

        try
        {
            _client.SetPresence(DiscordPresenceFormatter.Format(_cachedActivity.Request, _cachedActivity.State));
            _lastPublishedActivity = _cachedActivity;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not publish playback activity");
        }
    }

    private static bool ShouldPublish(CachedActivity? previous, CachedActivity current)
    {
        if (previous is null || previous.Request != current.Request) return true;
        var prior = previous.State;
        var next = current.State;
        if (prior.PlaylistIndex != next.PlaylistIndex || prior.IsPaused != next.IsPaused ||
            prior.Duration != next.Duration || Math.Abs(prior.Speed - next.Speed) > 0.001) return true;
        if (next.IsPaused) return false;

        var elapsed = next.ObservedAt - prior.ObservedAt;
        var expectedPosition = prior.Position + TimeSpan.FromTicks((long)(elapsed.Ticks * prior.Speed));
        return (next.Position - expectedPosition).Duration() >= TimeSpan.FromSeconds(2);
    }

    private void ClearAndDisposeClientLocked()
    {
        ClearPresenceLocked();

        var client = _client;
        _client = null;
        _lastPublishedActivity = null;
        _clientReady = false;
        if (client is null) return;
        UnsubscribeClientEvents(client);
        DisposeClientQuietly(client);
    }

    private void ClearPresenceLocked()
    {
        if (_client is null) return;

        try
        {
            _client.ClearPresence();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not clear playback activity");
        }
    }

    private static void DisposeClientQuietly(IDiscordRpcClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not dispose RPC client");
        }
    }


    private sealed record CachedActivity(PlaybackRequest Request, PlaybackPresenceState State);
}

internal static class DiscordPresenceFormatter
{
    private const int DiscordTextLimit = 128;
    private const int AssetKeyLimit = 256;
    private const int ButtonUrlLimit = 512;

    internal static RichPresence Format(PlaybackRequest request, PlaybackPresenceState playback)
    {
        if (request.Videos.IsDefaultOrEmpty)
            throw new InvalidOperationException("No video is available for Discord presence.");

        var videoIndex = playback.PlaylistIndex is >= 0 and < int.MaxValue &&
                         playback.PlaylistIndex < request.Videos.Length
            ? playback.PlaylistIndex
            : 0;
        var video = request.Videos[videoIndex];
        var title = TrimOptional(video.Title, DiscordTextLimit);
        var channelName = TrimOptional(video.ChannelName, DiscordTextLimit);
        var channelState = channelName is null ? null : TrimToUtf16($"by {channelName}", DiscordTextLimit);

        var presence = new RichPresence
        {
            Type = ActivityType.Watching,
            Details = title,
            State = playback.IsPaused
                ? channelState is null ? "Paused" : TrimToUtf16($"Paused · {channelState}", DiscordTextLimit)
                : channelState
        };

        if (!playback.IsPaused)
        {
            var speed = double.IsFinite(playback.Speed) && playback.Speed > 0 ? playback.Speed : 1;
            var start = playback.ObservedAt - ScaleForSpeed(playback.Position, speed);
            presence.Timestamps = new Timestamps
            {
                Start = start.UtcDateTime,
                End = playback.Duration > TimeSpan.Zero
                    ? (start + ScaleForSpeed(playback.Duration, speed)).UtcDateTime
                    : null
            };
        }

        if (IsHttpUrlWithinLimit(video.ThumbnailUrl, AssetKeyLimit))
            presence.Assets = new Assets
            {
                LargeImageKey = video.ThumbnailUrl,
                LargeImageText = title
            };

        var playbackUrl = string.IsNullOrWhiteSpace(video.WatchUrl)
            ? PlaybackRequest.BuildWatchUrl(video.Id)
            : video.WatchUrl;
        if (IsHttpUrlWithinLimit(playbackUrl, ButtonUrlLimit))
            presence.Buttons =
            [
                new Button
                {
                    Label = "Watch on YouTube",
                    Url = playbackUrl
                }
            ];

        return presence;
    }

    private static TimeSpan ScaleForSpeed(TimeSpan value, double speed)
    {
        return TimeSpan.FromTicks((long)(value.Ticks / speed));
    }

    private static bool IsHttpUrlWithinLimit(string? value, int limit)
    {
        return value is not null
               && value.Length <= limit
               && Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? TrimOptional(string? value, int limit)
    {
        return string.IsNullOrWhiteSpace(value) ? null : TrimToUtf16(value.Trim(), limit);
    }

    private static string TrimToUtf16(string value, int limit)
    {
        if (value.Length <= limit) return value;

        var length = limit;
        if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
        return value[..length];
    }
}