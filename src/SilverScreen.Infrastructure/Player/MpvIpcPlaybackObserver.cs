using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.Player;

internal sealed class MpvIpcPlaybackObserver : IDisposable
{
    private const int PropertyRequestIdOffset = 100;
    private static readonly ILogger Logger = Log.ForContext<MpvIpcPlaybackObserver>();
    private static readonly string[] ObservedProperties = ["time-pos", "duration", "pause", "playlist-pos", "speed"];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _endpoint;
    private readonly DirectoryInfo _endpointDirectory;
    private readonly Task _observation;
    private readonly Process _process;
    private readonly Action<PlaybackPresenceState> _stateChanged;
    private int _disposed;

    public MpvIpcPlaybackObserver(Process process, string endpoint, DirectoryInfo endpointDirectory,
        Action<PlaybackPresenceState> stateChanged)
    {
        _process = process;
        _endpoint = endpoint;
        _endpointDirectory = endpointDirectory;
        _stateChanged = stateChanged;
        _observation = ObserveAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cancellation.Cancel();
        DeleteEndpoint();
        ObserveCompletionAsync().FireAndForget(Logger);
    }

    private async Task ObserveCompletionAsync()
    {
        try
        {
            await _observation.ConfigureAwait(false);
        }
        catch
        {
            // The observer is best-effort and must never affect MPV process cleanup.
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            using var socket = await ConnectAsync(_cancellation.Token).ConfigureAwait(false);
            if (socket is null) return;

            await using var stream = new NetworkStream(socket, false);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.AutoFlush = true;
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await SubscribeAsync(writer, _cancellation.Token).ConfigureAwait(false);

            var state = PlaybackPresenceState.CreateInitial(DateTimeOffset.UtcNow);
            var hasPause = false;
            var hasTimeline = false;
            var hasPlaylistPosition = false;
            while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } line)
            {
                if (!MpvIpcPlaybackProtocol.TryApply(line, ref state, out var property)) continue;
                switch (property)
                {
                    case "pause":
                        hasPause = true;
                        break;
                    case "time-pos" or "duration":
                        hasTimeline = true;
                        break;
                    case "playlist-pos":
                        hasPlaylistPosition = true;
                        break;
                }

                if (hasPause && hasTimeline && hasPlaylistPosition) _stateChanged(state);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not observe external MPV playback through IPC at {Endpoint}", _endpoint);
        }
        finally
        {
            DeleteEndpoint();
        }
    }

    private async Task<Socket?> ConnectAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), cancellationToken)
                    .ConfigureAwait(false);
                return socket;
            }
            catch (SocketException)
            {
                socket.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static async Task SubscribeAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        for (var index = 0; index < ObservedProperties.Length; index++)
        {
            var property = ObservedProperties[index];
            await RequestPropertyAsync(writer, property, index, cancellationToken).ConfigureAwait(false);
            var observation = $"{{\"command\":[\"observe_property\",{index},\"{property}\"]}}";
            await writer.WriteLineAsync(observation.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task RequestPropertyAsync(StreamWriter writer, string property, int index,
        CancellationToken cancellationToken)
    {
        var request =
            $"{{\"command\":[\"get_property\",\"{property}\"],\"request_id\":{PropertyRequestIdOffset + index}}}";
        return writer.WriteLineAsync(request.AsMemory(), cancellationToken);
    }

    private void DeleteEndpoint()
    {
        try
        {
            File.Delete(_endpoint);
            _endpointDirectory.Delete(true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class MpvIpcPlaybackProtocol
{
    public static bool TryApply(string message, ref PlaybackPresenceState state, out string? property)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            property = null;
            return false;
        }

        var maxBytes = Encoding.UTF8.GetMaxByteCount(message.Length);
        if (maxBytes <= 512)
        {
            Span<byte> stackBuffer = stackalloc byte[512];
            var written = Encoding.UTF8.GetBytes(message, stackBuffer);
            return TryApply(stackBuffer[..written], ref state, out property);
        }

        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var written = Encoding.UTF8.GetBytes(message, rented);
            return TryApply(rented.AsSpan(0, written), ref state, out property);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static bool TryApply(ReadOnlySpan<byte> utf8Message, ref PlaybackPresenceState state, out string? property)
    {
        property = null;
        try
        {
            var reader = new Utf8JsonReader(utf8Message);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            var isPropertyChange = false;
            string? eventPropertyName = null;
            int? requestId = null;
            var hasData = false;
            var dataTokenType = JsonTokenType.None;
            double doubleValue = 0;
            var intValue = 0;
            var hasIntValue = false;
            var boolValue = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("event"u8))
                {
                    if (!reader.Read()) return false;
                    isPropertyChange = reader.TokenType == JsonTokenType.String &&
                                       reader.ValueTextEquals("property-change"u8);
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        reader.Skip();
                }
                else if (reader.ValueTextEquals("name"u8))
                {
                    if (!reader.Read()) return false;
                    if (reader.TokenType == JsonTokenType.String)
                        eventPropertyName = MatchKnownProperty(ref reader);
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        reader.Skip();
                }
                else if (reader.ValueTextEquals("request_id"u8))
                {
                    if (!reader.Read()) return false;
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var parsedRequestId))
                        requestId = parsedRequestId;
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        reader.Skip();
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!reader.Read()) return false;
                    dataTokenType = reader.TokenType;
                    switch (dataTokenType)
                    {
                        case JsonTokenType.Number:
                            hasData = reader.TryGetDouble(out doubleValue);
                            hasIntValue = reader.TryGetInt32(out intValue);
                            break;
                        case JsonTokenType.True:
                            hasData = true;
                            boolValue = true;
                            break;
                        case JsonTokenType.False:
                            hasData = true;
                            boolValue = false;
                            break;
                        case JsonTokenType.StartObject or JsonTokenType.StartArray:
                            reader.Skip();
                            break;
                    }
                }
                else
                {
                    if (!reader.Read()) return false;
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        reader.Skip();
                }
            }

            if (!hasData)
                return false;

            property = isPropertyChange
                ? eventPropertyName
                : requestId.HasValue
                    ? PropertyForRequestId(requestId.Value)
                    : null;

            if (property is null)
                return false;

            var observedAt = DateTimeOffset.UtcNow;
            switch (property)
            {
                case "time-pos" when dataTokenType == JsonTokenType.Number && double.IsFinite(doubleValue) &&
                                     doubleValue >= 0:
                    state = state with { Position = TimeSpan.FromSeconds(doubleValue), ObservedAt = observedAt };
                    return true;
                case "duration" when dataTokenType == JsonTokenType.Number && double.IsFinite(doubleValue) &&
                                     doubleValue >= 0:
                    state = state with { Duration = TimeSpan.FromSeconds(doubleValue), ObservedAt = observedAt };
                    return true;
                case "pause" when dataTokenType is JsonTokenType.True or JsonTokenType.False:
                    state = state with { IsPaused = boolValue, ObservedAt = observedAt };
                    return true;
                case "playlist-pos" when dataTokenType == JsonTokenType.Number && hasIntValue:
                    state = state with { PlaylistIndex = intValue, ObservedAt = observedAt };
                    return true;
                case "speed" when dataTokenType == JsonTokenType.Number && double.IsFinite(doubleValue) &&
                                  doubleValue > 0:
                    state = state with { Speed = doubleValue, ObservedAt = observedAt };
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? MatchKnownProperty(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("time-pos"u8)) return "time-pos";
        if (reader.ValueTextEquals("duration"u8)) return "duration";
        if (reader.ValueTextEquals("pause"u8)) return "pause";
        if (reader.ValueTextEquals("playlist-pos"u8)) return "playlist-pos";
        if (reader.ValueTextEquals("speed"u8)) return "speed";
        return null;
    }

    private static string? PropertyForRequestId(int requestId)
    {
        return requestId switch
        {
            100 => "time-pos",
            101 => "duration",
            102 => "pause",
            103 => "playlist-pos",
            104 => "speed",
            _ => null
        };
    }
}