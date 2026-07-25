using SilverScreen.Core.Models;
using System.Text.Json.Serialization;

namespace SilverScreen.Infrastructure.YouTube;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowseRequestPayload))]
[JsonSerializable(typeof(AccountProfile))]
[JsonSerializable(typeof(RatingRequestPayload))]
internal sealed partial class YouTubeRequestJsonContext : JsonSerializerContext;

internal sealed class BrowseRequestPayload
{
    public required BrowseRequestContext Context { get; init; }
    public string? BrowseId { get; init; }
    public string? Continuation { get; init; }
}

internal sealed class RatingRequestPayload
{
    public required BrowseRequestContext Context { get; init; }
    public required RatingTarget Target { get; init; }
    public string? Params { get; init; }
}

internal sealed class RatingTarget
{
    public required string VideoId { get; init; }
}

internal sealed class BrowseRequestContext
{
    public required BrowseRequestClientContext Client { get; init; }
    public required BrowseRequestUserContext User { get; init; }
}

internal sealed class BrowseRequestClientContext
{
    public required string ClientName { get; init; }
    public required string ClientVersion { get; init; }
    public required string OriginalUrl { get; init; }
    public required string Hl { get; init; }
    public required string Gl { get; init; }
    public string? VisitorData { get; init; }
}

internal sealed class BrowseRequestUserContext
{
    public bool LockedSafetyMode { get; init; }
    public int? Authuser { get; init; }
}