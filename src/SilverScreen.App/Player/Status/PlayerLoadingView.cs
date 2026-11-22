using Gtk;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Status;

/// <summary>
///     Encapsulates the player loading spinner, YouTube stream delay indicator, and buffering pill badge.
/// </summary>
public partial class PlayerLoadingView : ViewBase<Box>
{
    private PlayerStreamDelayController? _streamDelayController;

    public void AttachPlayer(LibMpvPlayer player)
    {
        _streamDelayController = Lifetime.Own(
            new PlayerStreamDelayController(player, player_delay_revealer, player_delay_bar));
    }

    public void SetLoading(bool loading)
    {
        if (!loading)
            _streamDelayController?.Reset();

        player_loading_indicator.SetVisible(loading);
    }

    public void SetBuffering(bool buffering, int percent)
    {
        if (buffering)
        {
            player_buffering_label.SetText(percent > 0 ? $"Buffering {percent}%" : "Buffering…");
            player_buffering_indicator.SetVisible(true);
        }
        else
        {
            player_buffering_indicator.SetVisible(false);
        }
    }

    public void Reset()
    {
        _streamDelayController?.Reset();
        player_loading_indicator.SetVisible(false);
        player_buffering_indicator.SetVisible(false);
    }
}