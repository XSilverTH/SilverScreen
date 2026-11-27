using Gtk;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controls;

/// <summary>
///     Lightweight presentation controller that binds UI like/dislike buttons and labels
///     to the underlying <see cref="PlaybackSession" /> engagement state and events.
/// </summary>
internal sealed class PlayerEngagementController : IDisposable
{
    private readonly Button _dislikeButton;
    private readonly Image _dislikeImage;
    private readonly Label _dislikesLabel;
    private readonly DisposeScope _lifetime = new();
    private readonly Button _likeButton;
    private readonly Image _likeImage;
    private readonly Label _likesLabel;
    private readonly PlaybackSession _session;

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

        _lifetime.Track(() => _session.EngagementChanged += OnEngagementChanged,
            () => _session.EngagementChanged -= OnEngagementChanged);
        _lifetime.Track(() => _session.RatingStateChanged += OnRatingStateChanged,
            () => _session.RatingStateChanged -= OnRatingStateChanged);
        _lifetime.Track(() => _session.VideoChanged += OnVideoChanged, () => _session.VideoChanged -= OnVideoChanged);
        _lifetime.Track(() => _session.SessionEnded += OnSessionEnded, () => _session.SessionEnded -= OnSessionEnded);
        _lifetime.Track(() => _session.Failed += OnSessionFailed, () => _session.Failed -= OnSessionFailed);

        UpdateUi();
    }

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        _lifetime.Dispose();
        SetReactionSensitive(false);
    }

    private void OnEngagementChanged(VideoEngagement? engagement)
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, () =>
        {
            _likesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Likes));
            _dislikesLabel.SetText(engagement is null ? "—" : FormatCount(engagement.Dislikes));
        });
    }

    private void OnRatingStateChanged(YouTubeRatingState ratingState)
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, () =>
        {
            SetRatingState(ratingState);
            SetReactionSensitive(_session.CanVote);
        });
    }

    private void OnVideoChanged(VideoSummary video, int playlistIndex)
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, () =>
        {
            _likesLabel.SetText("—");
            _dislikesLabel.SetText("—");
            SetRatingState(YouTubeRatingState.None);
            SetReactionSensitive(_session.CanVote);
        });
    }

    private void OnSessionEnded()
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, Clear);
    }

    private void OnSessionFailed(string detail)
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, Clear);
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
        var isLiked = ratingState == YouTubeRatingState.Like;
        _likeImage.SetFromResource(isLiked
            ? "/SilverScreen/Assets/liked-symbolic.svg"
            : "/SilverScreen/Assets/like-symbolic.svg");
        if (isLiked)
            _likeButton.AddCssClass("accent");
        else
            _likeButton.RemoveCssClass("accent");

        _dislikeImage.SetFromResource(ratingState == YouTubeRatingState.Dislike
            ? "/SilverScreen/Assets/disliked-symbolic.svg"
            : "/SilverScreen/Assets/dislike-symbolic.svg");
    }

    private static string FormatCount(long value)
    {
        return value.ToString("N0");
    }
}