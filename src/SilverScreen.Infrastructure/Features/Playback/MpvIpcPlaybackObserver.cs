using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Playback;

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
        _ = ObserveCompletionAsync();
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

            using var pollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            var polling = PollPropertiesAsync(writer, pollingCancellation.Token);
            try
            {
                var state = PlaybackPresenceState.CreateInitial(DateTimeOffset.UtcNow);
                var hasPause = false;
                var hasTimeline = false;
                while (await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false) is { } line)
                {
                    var previousPosition = state.Position;
                    if (!MpvIpcPlaybackProtocol.TryApply(line, ref state, out var property)) continue;
                    switch (property)
                    {
                        case "pause":
                            hasPause = true;
                            break;
                        case "time-pos" or "duration":
                            hasTimeline = true;
                            break;
                    }

                    if (property == "time-pos" && state.Position > previousPosition)
                        state = state with { IsPaused = false };
                    if (hasPause && hasTimeline) _stateChanged(state);
                }
            }
            finally
            {
                await pollingCancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await polling.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (pollingCancellation.IsCancellationRequested)
                {
                }
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

    private static async Task PollPropertiesAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            for (var index = 0; index < ObservedProperties.Length; index++)
                await RequestPropertyAsync(writer, ObservedProperties[index], index, cancellationToken)
                    .ConfigureAwait(false);
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
            _endpointDirectory.Delete();
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
        property = null;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.TryGetProperty("event", out var eventName) && eventName.GetString() == "property-change")
            {
                if (!root.TryGetProperty("name", out var name) || !root.TryGetProperty("data", out var data))
                    return false;
                property = name.GetString();
                return property is not null && TryApplyProperty(property, data, ref state);
            }

            if (!root.TryGetProperty("request_id", out var requestId) ||
                !root.TryGetProperty("data", out var responseData) ||
                !requestId.TryGetInt32(out var requestIdValue)) return false;
            property = PropertyForRequestId(requestIdValue);
            return property is not null && TryApplyProperty(property, responseData, ref state);
        }
        catch (JsonException)
        {
            return false;
        }
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

    private static bool TryApplyProperty(string property, JsonElement value, ref PlaybackPresenceState state)
    {
        var observedAt = DateTimeOffset.UtcNow;
        switch (property)
        {
            case "time-pos" when value.TryGetDouble(out var position) && double.IsFinite(position) && position >= 0:
                state = state with { Position = TimeSpan.FromSeconds(position), ObservedAt = observedAt };
                return true;
            case "duration" when value.TryGetDouble(out var duration) && double.IsFinite(duration) && duration >= 0:
                state = state with { Duration = TimeSpan.FromSeconds(duration), ObservedAt = observedAt };
                return true;
            case "pause" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                state = state with { IsPaused = value.GetBoolean(), ObservedAt = observedAt };
                return true;
            case "playlist-pos" when value.TryGetInt32(out var playlistIndex):
                state = state with { PlaylistIndex = playlistIndex, ObservedAt = observedAt };
                return true;
            case "speed" when value.TryGetDouble(out var speed) && double.IsFinite(speed) && speed > 0:
                state = state with { Speed = speed, ObservedAt = observedAt };
                return true;
            default:
                return false;
        }
    }
}