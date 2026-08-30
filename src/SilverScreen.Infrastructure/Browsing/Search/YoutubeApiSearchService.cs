using Serilog;
using SilverScreen.Core.Browsing.Common;
using CoreSearchRequest = SilverScreen.Core.Browsing.Search.SearchRequest;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.Enums;
using YoutubeAPI.Models.Search;
using ApiVideoSummary = YoutubeAPI.Models.Videos.VideoSummary;

namespace SilverScreen.Infrastructure.Browsing.Search;

/// <summary>Searches YouTube through the configured YoutubeAPI client.</summary>
public sealed class YoutubeApiSearchService(IYouTubeClientProvider clientProvider) : ISearchService
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiSearchService>();

    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    public async Task<SearchResultPage> SearchAsync(CoreSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return SearchResultPage.Empty;

        var pageSize = Math.Max(request.Count, 1);
        try
        {
            var client = _clientProvider.GetClient();
            var page = request.ContinuationToken is null
                ? await client.Search.GetPageAsync(
                    new YoutubeAPI.Models.Search.SearchRequest(request.Query, SearchKind.Video),
                    cancellationToken).ConfigureAwait(false)
                : await client.Search.GetPageAsync(
                    SearchContinuation.Import(request.ContinuationToken), cancellationToken)
                    .ConfigureAwait(false);

            var videos = page.Items
                .OfType<VideoSearchResult>()
                .Where(result => !result.Video.IsShort)
                .Take(pageSize)
                .Select(result => MapVideo(result.Video, result.PlaybackProgress))
                .ToArray();
            var continuationToken = page.Next?.Export();

            return videos.Length == 0
                ? new SearchResultPage(videos, "No video results found.", ContinuationToken: continuationToken)
                : new SearchResultPage(
                    videos,
                    $"Found {videos.Length} video result{(videos.Length == 1 ? string.Empty : "s")}.",
                    ContinuationToken: continuationToken);
        }
        catch (FormatException exception)
        {
            Logger.Warning(exception, "Invalid YouTube search continuation");
            return SearchResultPage.Failed($"Search failed: invalid continuation ({exception.Message}).");
        }
        catch (AuthenticationRequiredException exception)
        {
            Logger.Warning(exception, "YouTube search requires authentication");
            return SearchResultPage.Failed("Search failed: YouTube authentication is required.");
        }
        catch (AuthenticationExpiredException exception)
        {
            Logger.Warning(exception, "YouTube search authentication expired");
            return SearchResultPage.Failed("Search failed: the YouTube session has expired.");
        }
        catch (PermissionDeniedException exception)
        {
            Logger.Warning(exception, "YouTube denied search request");
            return SearchResultPage.Failed("Search failed: YouTube denied the request.");
        }
        catch (ResourceNotFoundException exception)
        {
            Logger.Warning(exception, "YouTube search resource was not found");
            return SearchResultPage.Failed("Search failed: the requested resource was not found.");
        }
        catch (RateLimitedException exception)
        {
            Logger.Warning(exception, "YouTube search was rate limited");
            return SearchResultPage.Failed("Search failed: YouTube rate limit reached.");
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YouTube search request failed");
            return SearchResultPage.Failed($"Search failed: {exception.Message}");
        }
    }

    private static VideoSummary MapVideo(
        ApiVideoSummary video,
        YoutubeAPI.Models.Videos.VideoPlaybackProgress? playbackProgress)
    {
        var thumbnailUrl = video.Thumbnails
            .OrderBy(thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .LastOrDefault()?.Url.ToString() ?? string.Empty;
        var publishedAt = video.PublishedAt;
        var approximateUploadDate = publishedAt is { } value
            ? (DateOnly?)DateOnly.FromDateTime(value.DateTime)
            : null;

        return new VideoSummary(
            video.Id.Value,
            video.Title,
            video.Channel.Title,
            video.Duration ?? TimeSpan.Zero,
            thumbnailUrl,
            video.IsShort,
            video.Url.ToString(),
            approximateUploadDate,
            publishedAt,
            video.Channel.Url.ToString(),
            YouTubePlaybackProgressMapper.Map(playbackProgress));
    }
}
