using System.Text.Json.Serialization;

namespace SilverScreen.Infrastructure.YouTube;

internal sealed class YtDlpIpcRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public string[]? Args { get; set; }
}

internal sealed class YtDlpIpcResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("python")]
    public string? Python { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

[JsonSerializable(typeof(YtDlpIpcRequest))]
[JsonSerializable(typeof(YtDlpIpcResponse))]
internal sealed partial class YtDlpIpcJsonContext : JsonSerializerContext;
