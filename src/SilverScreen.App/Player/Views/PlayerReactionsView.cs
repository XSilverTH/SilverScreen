using Gtk;
using SilverScreen.Core.Player;
using SilverScreen.Player.Controllers;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Views;

/// <summary>
///     Encapsulates like and dislike reaction buttons, count labels, and engagement state.
/// </summary>
public partial class PlayerReactionsView : ViewBase<Box>
{
    private PlaybackSession? _session;

    internal void AttachSession(PlaybackSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Lifetime.Own(new PlayerEngagementController(
            _session,
            player_like_button,
            player_like_image,
            player_likes_label,
            player_dislike_button,
            player_dislike_image,
            player_dislikes_label));
    }

    private void OnLikeButtonClicked(object? sender, EventArgs args)
    {
        _session?.SubmitVote(VideoVote.Like);
    }

    private void OnDislikeButtonClicked(object? sender, EventArgs args)
    {
        _session?.SubmitVote(VideoVote.Dislike);
    }
}
