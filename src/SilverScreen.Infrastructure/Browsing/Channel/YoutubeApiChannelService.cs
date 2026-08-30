using Serilog;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.ValueTypes;
using ApiChannelVideoSort = YoutubeAPI.Models.Enums.ChannelVideoSort;
using ApiVideoSummary = YoutubeAPI.Models.Videos.VideoSummary;

namespace SilverScreen.Infrastructure.Browsing.Channel;

/// <summary>Loads YouTube channel metadata and videos through the configured YoutubeAPI client.</summary>
public sealed class YoutubeApiChannelService(IYouTubeClientProvider clientProvider) : IChannelService
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiChannelService>();

    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    public async Task<ChannelPage> GetChannelAsync(
        string channelUrl,
        string fallbackName,
        ChannelVideoSort sort,
        string? continuationToken,
        int count,
        CancellationToken cancellationToken)
    {

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(channelUrl);
            var pageSize = Math.Max(count, 1);
            var channelReference = ChannelReference.Parse(channelUrl);
            var client = _clientProvider.GetClient();
            var continuation = continuationToken is null
                ? null
                : ChannelVideosContinuation.Import(continuationToken);
            var apiSort = continuation is null ? ToApiSort(sort) : continuation.Sort;

            var metadata = await client.Channels.GetAsync(channelReference, cancellationToken)
                .ConfigureAwait(false);
            var videosPage = continuation is null
                ? await client.Channels.GetVideosPageAsync(channelReference, apiSort, cancellationToken)
                    .ConfigureAwait(false)
                : await client.Channels.GetVideosPageAsync(continuation, cancellationToken)
                    .ConfigureAwait(false);

            var videos = videosPage.Items
                .Where(video => !video.IsShort)
                .DistinctBy(video => video.Id)
                .Take(pageSize)
                .Select(video => MapVideo(
                    video,
                    videosPage.PlaybackProgress?.GetValueOrDefault(video.Id)))
                .ToArray();
            var nextContinuationToken = videosPage.Next?.Export();
            var resultSort = ToCoreSort(apiSort);
            var status = videos.Length == 0 ? "This channel has no videos to show." : null;

            return new ChannelPage(
                channelUrl,
                string.IsNullOrWhiteSpace(metadata.Summary.Title) ? fallbackName : metadata.Summary.Title,
                string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description,
                SelectThumbnail(metadata.Summary.Thumbnails),
                metadata.Summary.SubscriberCount,
                videos,
                resultSort,
                status,
                IsSuccess: true,
                NextContinuationToken: nextContinuationToken);
        }
        catch (FormatException exception)
        {
            Logger.Warning(exception, "Invalid YouTube channel or continuation reference");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: invalid channel or continuation ({exception.Message}).");
        }
        catch (ArgumentException exception)
        {
            Logger.Warning(exception, "Invalid YouTube channel reference");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: invalid channel reference ({exception.Message}).");
        }
        catch (AuthenticationRequiredException exception)
        {
            Logger.Warning(exception, "YouTube channel request requires authentication");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                "Could not load channel: YouTube authentication is required.");
        }
        catch (AuthenticationExpiredException exception)
        {
            Logger.Warning(exception, "YouTube channel authentication expired");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                "Could not load channel: the YouTube session has expired.");
        }
        catch (PermissionDeniedException exception)
        {
            Logger.Warning(exception, "YouTube denied channel request");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                "Could not load channel: YouTube denied the request.");
        }
        catch (ResourceNotFoundException exception)
        {
            Logger.Warning(exception, "YouTube channel was not found");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                "Could not load channel: the channel was not found.");
        }
        catch (RateLimitedException exception)
        {
            Logger.Warning(exception, "YouTube channel request was rate limited");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                "Could not load channel: YouTube rate limit reached.");
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YouTube channel request failed");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: {exception.Message}");
        }
    }

    private static ApiChannelVideoSort ToApiSort(ChannelVideoSort sort)
    {
        return sort switch
        {
            ChannelVideoSort.Newest => ApiChannelVideoSort.Newest,
            ChannelVideoSort.Oldest => ApiChannelVideoSort.Oldest,
            ChannelVideoSort.Popular => ApiChannelVideoSort.Popular,
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown channel video sort.")
        };
    }

    private static ChannelVideoSort ToCoreSort(ApiChannelVideoSort sort)
    {
        return sort switch
        {
            ApiChannelVideoSort.Newest => ChannelVideoSort.Newest,
            ApiChannelVideoSort.Oldest => ChannelVideoSort.Oldest,
            ApiChannelVideoSort.Popular => ChannelVideoSort.Popular,
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown channel video sort.")
        };
    }

    private static VideoSummary MapVideo(
        ApiVideoSummary video,
        YoutubeAPI.Models.Videos.VideoPlaybackProgress? playbackProgress)
    {
        var thumbnailUrl = SelectThumbnail(video.Thumbnails) ?? string.Empty;
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

    private static string? SelectThumbnail(IReadOnlyList<YoutubeAPI.Models.Common.Thumbnail> thumbnails)
    {
        return thumbnails
            .OrderBy(thumbnail => (long)thumbnail.Width * thumbnail.Height)
            .LastOrDefault()?.Url.ToString();
    }
}
