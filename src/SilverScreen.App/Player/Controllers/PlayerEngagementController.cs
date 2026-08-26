using Gtk;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Lightweight presentation controller that binds UI like/dislike buttons and labels
///     to the underlying <see cref="PlaybackSession" /> engagement state and events.
/// </summary>
internal sealed class PlayerEngagementController : IDisposable
{
    private readonly Button _dislikeButton;
    private readonly Image _dislikeImage;
    private readonly Label _dislikesLabel;
    private readonly Button _likeButton;
    private readonly Image _likeImage;
    private readonly Label _likesLabel;
    private readonly PlaybackSession _session;
    private bool _disposed;

    public PlayerEngagementController(
        PlaybackSession session,
        Button likeButton,
        Image likeImage,
        Label likesLabel,
        Button dislikeButton,
        Image dislikeImage,
        Label dislikesLabel)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _likeButton = likeButton;
        _likeImage = likeImage;
        _likesLabel = likesLabel;
        _dislikeButton = dislikeButton;
        _dislikeImage = dislikeImage;
        _dislikesLabel = dislikesLabel;

        _session.EngagementChanged += OnEngagementChanged;
        _session.RatingStateChanged += OnRatingStateChanged;
        _session.VideoChanged += OnVideoChanged;
        _session.SessionEnded += OnSessionEnded;
        _session.Failed += OnSessionFailed;

        UpdateUi();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.EngagementChanged -= OnEngagementChanged;
        _session.RatingStateChanged -= OnRatingStateChanged;
        _session.VideoChanged -= OnVideoChanged;
        _session.SessionEnded -= OnSessionEnded;
        _session.Failed -= OnSessionFailed;
        SetReactionSensitive(false);
    }

    private void OnEngagementChanged(VideoEngagement? engagement)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            _likesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Likes));
            _dislikesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Dislikes));
            return false;
        });
    }

    private void OnRatingStateChanged(YouTubeRatingState ratingState)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            SetRatingState(ratingState);
            SetReactionSensitive(_session.CanVote);
            return false;
        });
    }

    private void OnVideoChanged(VideoSummary video, int playlistIndex)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            _likesLabel.SetText("—");
            _dislikesLabel.SetText("—");
            SetRatingState(YouTubeRatingState.None);
            SetReactionSensitive(_session.CanVote);
            return false;
        });
    }

    private void OnSessionEnded()
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            Clear();
            return false;
        });
    }

    private void OnSessionFailed(string detail)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            Clear();
            return false;
        });
    }

    private void Clear()
    {
        _likesLabel.SetText("—");
        _dislikesLabel.SetText("—");
        SetRatingState(YouTubeRatingState.None);
        SetReactionSensitive(false);
    }

    private void UpdateUi()
    {
        SetReactionSensitive(_session.CanVote);
        SetRatingState(_session.RatingState);
        _likesLabel.SetText(_session.Engagement is null ? "—" : FormatCount(_session.Engagement.Likes));
        _dislikesLabel.SetText(_session.Engagement is null ? "—" : FormatCount(_session.Engagement.Dislikes));
    }

    private void SetReactionSensitive(bool sensitive)
    {
        _likeButton.SetSensitive(sensitive);
        _dislikeButton.SetSensitive(sensitive);
    }

    private void SetRatingState(YouTubeRatingState ratingState)
    {
        _likeImage.SetFromResource(ratingState == YouTubeRatingState.Like
            ? "/SilverScreen/Assets/liked-symbolic.svg"
            : "/SilverScreen/Assets/like-symbolic.svg");
        _dislikeImage.SetFromResource(ratingState == YouTubeRatingState.Dislike
            ? "/SilverScreen/Assets/disliked-symbolic.svg"
            : "/SilverScreen/Assets/dislike-symbolic.svg");
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }
}