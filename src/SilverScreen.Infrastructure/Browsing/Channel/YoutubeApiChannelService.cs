using System.Collections.Concurrent;
using Serilog;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.ValueTypes;
using YoutubeAPI.Models.Videos;
using ApiChannelVideoSort = YoutubeAPI.Models.Enums.ChannelVideoSort;
using ApiVideoSummary = YoutubeAPI.Models.Videos.VideoSummary;
using VideoSummary = SilverScreen.Core.Domain.VideoSummary;

namespace SilverScreen.Infrastructure.Browsing.Channel;

/// <summary>
///     Loads YouTube channel metadata and videos through the configured YoutubeAPI client,
///     caching channel metadata and initial pages briefly to eliminate redundant round-trips.
/// </summary>
public sealed class YoutubeApiChannelService(
    IYouTubeClientProvider clientProvider,
    TimeProvider? timeProvider = null) : IChannelService
{
    private const int MaxCachedChannels = 32;
    private const int MaxCachedFirstPages = 20;
    private static readonly TimeSpan MetadataCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstPageCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiChannelService>();

    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    private readonly BoundedLruCache<(string ChannelKey, ChannelVideoSort Sort), CachedFirstPage> _firstPageCache =
        new(MaxCachedFirstPages, timeProvider: timeProvider);

    private readonly ConcurrentDictionary<(string ChannelKey, ChannelVideoSort Sort), Lazy<Task<ChannelPage>>>
        _inFlightFirstPages = new();

    private readonly BoundedLruCache<string, CachedChannelMetadata> _metadataCache =
        new(MaxCachedChannels, StringComparer.Ordinal, timeProvider);

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
            var channelReference = ChannelReference.Parse(channelUrl);
            var client = _clientProvider.GetClient();
            var continuation = continuationToken is null
                ? null
                : ChannelVideosContinuation.Import(continuationToken);
            var apiSort = continuation?.Sort ?? ToApiSort(sort);
            var channelKey = channelReference.Value;

            if (continuation is null)
            {
                if (_firstPageCache.TryGetValue((channelKey, sort), out var cachedPage) &&
                    ReferenceEquals(cachedPage.Client, client))
                {
                    Logger.Debug("Channel first-page cache hit");
                    return cachedPage.Page;
                }

                return await LoadFirstPageCoalescedAsync(
                    client,
                    channelReference,
                    channelKey,
                    channelUrl,
                    fallbackName,
                    sort,
                    apiSort,
                    cancellationToken).ConfigureAwait(false);
            }

            var videosPage = await client.Channels.GetVideosPageAsync(continuation, cancellationToken)
                .ConfigureAwait(false);
            return BuildChannelPage(
                channelUrl,
                fallbackName,
                null,
                null,
                null,
                apiSort,
                videosPage);
        }
        catch (FormatException exception)
        {
            Logger.Warning(exception, "Invalid YouTube channel or continuation reference");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (ArgumentException exception)
        {
            Logger.Warning(exception, "Invalid YouTube channel reference");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (AuthenticationRequiredException exception)
        {
            Logger.Warning(exception, "YouTube channel request requires authentication");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (AuthenticationExpiredException exception)
        {
            Logger.Warning(exception, "YouTube channel authentication expired");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (PermissionDeniedException exception)
        {
            Logger.Warning(exception, "YouTube denied channel request");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (ResourceNotFoundException exception)
        {
            Logger.Warning(exception, "YouTube channel was not found");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (RateLimitedException exception)
        {
            Logger.Warning(exception, "YouTube channel request was rate limited");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YouTube channel request failed");
            return ChannelPage.Failed(channelUrl, fallbackName, sort, "Could not load channel.");
        }
    }

    private async Task<ChannelPage> LoadFirstPageCoalescedAsync(
        YouTubeClient client,
        ChannelReference channelReference,
        string channelKey,
        string channelUrl,
        string fallbackName,
        ChannelVideoSort sort,
        ApiChannelVideoSort apiSort,
        CancellationToken cancellationToken)
    {
        var flightKey = (channelKey, sort);
        Lazy<Task<ChannelPage>>? created = null;
        var lazy = _inFlightFirstPages.GetOrAdd(flightKey, _ =>
        {
            created = new Lazy<Task<ChannelPage>>(
                () => ExecuteFirstPageAsync(
                    client,
                    channelReference,
                    channelKey,
                    channelUrl,
                    fallbackName,
                    sort,
                    apiSort,
                    flightKey,
                    created!),
                LazyThreadSafetyMode.ExecutionAndPublication);
            return created;
        });

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChannelPage> ExecuteFirstPageAsync(
        YouTubeClient client,
        ChannelReference channelReference,
        string channelKey,
        string channelUrl,
        string fallbackName,
        ChannelVideoSort sort,
        ApiChannelVideoSort apiSort,
        (string ChannelKey, ChannelVideoSort Sort) flightKey,
        Lazy<Task<ChannelPage>> self)
    {
        try
        {
            if (_firstPageCache.TryGetValue(flightKey, out var cachedPage) &&
                ReferenceEquals(cachedPage.Client, client))
                return cachedPage.Page;

            CachedChannelMetadata? metadata;
            if (!_metadataCache.TryGetValue(channelKey, out metadata) ||
                !ReferenceEquals(metadata.Client, client))
            {
                var metadataTask = client.Channels.GetAsync(channelReference, CancellationToken.None);
                var videosTask = client.Channels.GetVideosPageAsync(channelReference, apiSort, CancellationToken.None);
                await Task.WhenAll(metadataTask, videosTask).ConfigureAwait(false);

                var apiMetadata = await metadataTask.ConfigureAwait(false);
                var videosPage = await videosTask.ConfigureAwait(false);

                metadata = new CachedChannelMetadata(
                    client,
                    apiMetadata.Summary.Title,
                    string.IsNullOrWhiteSpace(apiMetadata.Description) ? null : apiMetadata.Description,
                    ThumbnailResolutionSelector.SelectAvatarThumbnail(apiMetadata.Summary.Thumbnails),
                    apiMetadata.Summary.SubscriberCount);
                _metadataCache.Set(channelKey, metadata, MetadataCacheTtl);

                var page = BuildChannelPage(
                    channelUrl,
                    string.IsNullOrWhiteSpace(metadata.Title) ? fallbackName : metadata.Title,
                    metadata.Description,
                    metadata.AvatarUrl,
                    metadata.SubscriberCount,
                    apiSort,
                    videosPage);
                _firstPageCache.Set(flightKey, new CachedFirstPage(client, page), FirstPageCacheTtl);
                return page;
            }
            else
            {
                var videosPage = await client.Channels
                    .GetVideosPageAsync(channelReference, apiSort, CancellationToken.None)
                    .ConfigureAwait(false);
                var page = BuildChannelPage(
                    channelUrl,
                    string.IsNullOrWhiteSpace(metadata.Title) ? fallbackName : metadata.Title,
                    metadata.Description,
                    metadata.AvatarUrl,
                    metadata.SubscriberCount,
                    apiSort,
                    videosPage);
                _firstPageCache.Set(flightKey, new CachedFirstPage(client, page), FirstPageCacheTtl);
                return page;
            }
        }
        finally
        {
            _inFlightFirstPages.TryRemove(
                new KeyValuePair<(string ChannelKey, ChannelVideoSort Sort), Lazy<Task<ChannelPage>>>(flightKey, self));
        }
    }

    private static ChannelPage BuildChannelPage(
        string channelUrl,
        string channelName,
        string? description,
        string? avatarUrl,
        long? subscriberCount,
        ApiChannelVideoSort apiSort,
        Page<ApiVideoSummary, ChannelVideosContinuation> videosPage)
    {
        var videos = videosPage.Items
            .Where(video => !video.IsShort)
            .DistinctBy(video => video.Id)
            .Select(video => MapVideo(
                video,
                channelName,
                videosPage.PlaybackProgress?.GetValueOrDefault(video.Id)))
            .ToArray();
        var nextContinuationToken = videosPage.Next?.Export();
        var resultSort = ToCoreSort(apiSort);
        var status = videos.Length == 0 ? "This channel has no videos to show." : null;

        return new ChannelPage(
            channelUrl,
            channelName,
            description,
            avatarUrl,
            subscriberCount,
            videos,
            resultSort,
            status,
            true,
            nextContinuationToken);
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
        string channelName,
        VideoPlaybackProgress? playbackProgress)
    {
        var thumbnailUrl = ThumbnailResolutionSelector.SelectCardThumbnail(video.Thumbnails);
        var publishedAt = video.PublishedAt;
        var approximateUploadDate = publishedAt is { } value
            ? (DateOnly?)DateOnly.FromDateTime(value.DateTime)
            : null;

        return new VideoSummary(
            video.Id.Value,
            video.Title,
            channelName,
            video.Duration ?? TimeSpan.Zero,
            thumbnailUrl,
            video.IsShort,
            video.Url.ToString(),
            approximateUploadDate,
            publishedAt,
            video.Channel.Url.ToString(),
            YouTubePlaybackProgressMapper.Map(playbackProgress));
    }

    private sealed record CachedChannelMetadata(
        YouTubeClient Client,
        string Title,
        string? Description,
        string? AvatarUrl,
        long? SubscriberCount);

    private sealed record CachedFirstPage(YouTubeClient Client, ChannelPage Page);
}