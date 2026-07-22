using DiscordRPC;
using DiscordRPC.Entities;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

internal interface IDiscordRpcClient : IDisposable
{
    bool Initialize();
    void SetPresence(RichPresence presence);
    void ClearPresence();
}

internal sealed class DiscordRpcClientAdapter(string applicationId) : IDiscordRpcClient
{
    private readonly DiscordRpcClient _client = new(applicationId);

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
    private readonly string? _applicationId;
    private readonly Func<string, IDiscordRpcClient> _clientFactory;
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private CachedActivity? _cachedActivity;

    private IDiscordRpcClient? _client;
    private bool _disposed;
    private bool _enabled;

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

    public void SetPlaying(PlaybackRequest request, DateTimeOffset startedAt)
    {
        lock (_lock)
        {
            if (_disposed) return;

            _cachedActivity = new CachedActivity(request, startedAt);
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

        try
        {
            if (!client.Initialize())
            {
                DisposeClientQuietly(client);
                return;
            }

            _client = client;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not initialize RPC client");
            DisposeClientQuietly(client);
        }
    }

    private void PublishCachedActivityLocked()
    {
        if (_client is null || _cachedActivity is null) return;

        try
        {
            _client.SetPresence(DiscordPresenceFormatter.Format(_cachedActivity.Request, _cachedActivity.StartedAt));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not publish playback activity");
        }
    }

    private void ClearAndDisposeClientLocked()
    {
        ClearPresenceLocked();

        var client = _client;
        _client = null;
        if (client is not null) DisposeClientQuietly(client);
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


    private sealed record CachedActivity(PlaybackRequest Request, DateTimeOffset StartedAt);
}

internal static class DiscordPresenceFormatter
{
    private const int DiscordTextLimit = 128;
    private const int AssetKeyLimit = 256;
    private const int ButtonUrlLimit = 512;

    internal static RichPresence Format(PlaybackRequest request, DateTimeOffset startedAt)
    {
        var video = request.Videos.IsDefaultOrEmpty
            ? throw new InvalidOperationException("No video is available for Discord presence.")
            : request.Videos[0];
        var title = TrimOptional(video.Title, DiscordTextLimit);
        var channelName = TrimOptional(video.ChannelName, DiscordTextLimit);
        var start = startedAt.UtcDateTime;

        var presence = new RichPresence
        {
            Type = ActivityType.Watching,
            Details = title,
            State = channelName is null ? null : TrimToUtf16($"by {channelName}", DiscordTextLimit),
            Timestamps = new Timestamps
            {
                Start = start,
                End = video.Duration > TimeSpan.Zero ? start + video.Duration : null
            }
        };

        if (IsHttpUrlWithinLimit(video.ThumbnailUrl, AssetKeyLimit))
            presence.Assets = new Assets
            {
                LargeImageKey = video.ThumbnailUrl,
                LargeImageText = title
            };

        if (IsHttpUrlWithinLimit(request.PlaybackUrl, ButtonUrlLimit))
            presence.Buttons =
            [
                new Button
                {
                    Label = "Watch on YouTube",
                    Url = request.PlaybackUrl!
                }
            ];

        return presence;
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