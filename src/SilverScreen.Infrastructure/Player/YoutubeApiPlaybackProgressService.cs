using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.ValueTypes;

namespace SilverScreen.Infrastructure.Player;

/// <summary>Reads viewer playback state from the current YoutubeAPI session without local persistence.</summary>
public sealed class YoutubeApiPlaybackProgressService(IYouTubeClientProvider clientProvider)
    : IYouTubePlaybackProgressService
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiPlaybackProgressService>();
    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    public async Task<YouTubePlaybackProgress?> GetAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        if (!VideoId.TryParse(videoId, out var parsedVideoId))
            return null;

        try
        {
            var progress = await _clientProvider.GetClient().Videos
                .GetPlaybackProgressAsync(parsedVideoId, cancellationToken)
                .ConfigureAwait(false);
            return YouTubePlaybackProgressMapper.Map(progress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException ex) when (
            ex is AuthenticationRequiredException or AuthenticationExpiredException or PermissionDeniedException)
        {
            Logger.Debug(ex, "YouTube playback progress is unavailable for {VideoId}", videoId);
            return null;
        }
        catch (YouTubeException ex)
        {
            Logger.Debug(ex, "Unable to load YouTube playback progress for {VideoId}", videoId);
            return null;
        }
    }
}
