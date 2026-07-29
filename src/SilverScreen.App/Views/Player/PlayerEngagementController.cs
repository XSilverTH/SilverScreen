using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using static GLib.Functions;

namespace SilverScreen.Views.Player;

internal sealed class PlayerEngagementController(
    IVideoEngagementService videoEngagement,
    IYouTubeRatingService youtubeRating,
    ISessionService session,
    Button likeButton,
    Image likeImage,
    Label likesLabel,
    Button dislikeButton,
    Image dislikeImage,
    Label dislikesLabel)
    : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlayerEngagementController>();
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private long _loadVersion;
    private YouTubeRatingState _ratingState;
    private string? _videoId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoad();
        _videoId = null;
        SetReactionSensitive(false);
    }

    public void Load(VideoSummary video)
    {
        if (_disposed) return;
        CancelLoad();
        _videoId = video.Id;
        likesLabel.SetText("—");
        dislikesLabel.SetText("—");
        SetRatingState(YouTubeRatingState.None);
        var validVideo = PlaybackRequest.LooksLikeYouTubeVideoId(video.Id);
        SetReactionSensitive(validVideo && session.GetCurrentSession().IsSignedIn);
        if (!validVideo) return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var version = ++_loadVersion;
        _ = UpdateEngagementAsync(video.Id, version, cancellation.Token);
        _ = UpdateRatingStateAsync(video.Id, version, cancellation.Token);
    }

    public void SubmitVote(VideoVote vote)
    {
        if (_videoId is not { } videoId || !PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return;
        var removeVote =
            _ratingState == (vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike);
        var version = _loadVersion;
        var token = _cancellation?.Token ?? CancellationToken.None;
        SetReactionSensitive(false);
        _ = SubmitVoteAsync(videoId, vote, removeVote, version, token);
    }

    public void Clear()
    {
        if (_disposed) return;
        CancelLoad();
        _videoId = null;
        likesLabel.SetText("—");
        dislikesLabel.SetText("—");
        SetRatingState(YouTubeRatingState.None);
    }

    private async Task UpdateEngagementAsync(string videoId, long version, CancellationToken cancellationToken)
    {
        VideoEngagement? engagement;
        try
        {
            engagement = await videoEngagement.GetEngagementAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to load engagement counts for {VideoId}", videoId);
            return;
        }

        IdleAdd(0, () =>
        {
            if (!IsCurrent(videoId, version, cancellationToken)) return false;
            likesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Likes));
            dislikesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Dislikes));
            return false;
        });
    }

    private async Task UpdateRatingStateAsync(string videoId, long version, CancellationToken cancellationToken)
    {
        YouTubeRatingState ratingState;
        try
        {
            ratingState = await youtubeRating.GetRatingStateAsync(videoId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to load the native YouTube rating for {VideoId}", videoId);
            return;
        }

        IdleAdd(0, () =>
        {
            if (!IsCurrent(videoId, version, cancellationToken)) return false;
            SetRatingState(ratingState);
            return false;
        });
    }

    private async Task SubmitVoteAsync(string videoId, VideoVote vote, bool removeVote, long version,
        CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            succeeded = removeVote
                ? await youtubeRating.RemoveVoteAsync(videoId, vote, cancellationToken).ConfigureAwait(false)
                : await youtubeRating.SubmitVoteAsync(videoId, vote, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unable to submit {Vote} for {VideoId}", vote, videoId);
        }

        IdleAdd(0, () =>
        {
            if (!IsCurrent(videoId, version, cancellationToken)) return false;
            SetReactionSensitive(true);
            if (!succeeded) return false;
            SetRatingState(removeVote ? YouTubeRatingState.None :
                vote == VideoVote.Like ? YouTubeRatingState.Like : YouTubeRatingState.Dislike);
            _ = UpdateEngagementAsync(videoId, version, CancellationToken.None);
            return false;
        });
    }

    private bool IsCurrent(string videoId, long version, CancellationToken cancellationToken)
    {
        return !_disposed && !cancellationToken.IsCancellationRequested && version == _loadVersion &&
               _videoId == videoId;
    }

    private void CancelLoad()
    {
        _loadVersion++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        SetReactionSensitive(false);
    }

    private void SetReactionSensitive(bool sensitive)
    {
        likeButton.SetSensitive(sensitive);
        dislikeButton.SetSensitive(sensitive);
    }

    private void SetRatingState(YouTubeRatingState ratingState)
    {
        _ratingState = ratingState;
        likeImage.SetFromResource(ratingState == YouTubeRatingState.Like
            ? "/SilverScreen/Assets/liked-symbolic.svg"
            : "/SilverScreen/Assets/like-symbolic.svg");
        dislikeImage.SetFromResource(ratingState == YouTubeRatingState.Dislike
            ? "/SilverScreen/Assets/disliked-symbolic.svg"
            : "/SilverScreen/Assets/dislike-symbolic.svg");
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }
}