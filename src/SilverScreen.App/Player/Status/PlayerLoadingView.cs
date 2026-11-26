using Gtk;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Status;

/// <summary>
///     Encapsulates the player loading spinner, YouTube stream delay indicator, and buffering pill badge.
/// </summary>
public partial class PlayerLoadingView : ViewBase<Box>
{
    private int _bufferingPercent = -1;
    private bool? _isBuffering;
    private bool? _isLoading;
    private PlayerStreamDelayController? _streamDelayController;

    public void AttachPlayer(LibMpvPlayer player)
    {
        _streamDelayController = Lifetime.Own(
            new PlayerStreamDelayController(player, player_delay_revealer, player_delay_bar));
    }

    public void SetLoading(bool loading)
    {
        if (_isLoading == loading && !(_isBuffering == true && loading))
            return;

        _isLoading = loading;
        if (!loading)
            _streamDelayController?.Reset();
        else if (_isBuffering == true)
            SetBuffering(false, 0);

        player_loading_indicator.SetVisible(loading);
    }

    public void SetBuffering(bool buffering, int percent)
    {
        if (_isBuffering == buffering && (!buffering || _bufferingPercent == percent))
            return;

        _isBuffering = buffering;
        _bufferingPercent = buffering ? percent : -1;

        if (buffering)
        {
            if (_isLoading == true)
            {
                _isLoading = false;
                player_loading_indicator.SetVisible(false);
            }

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
        if (_isLoading != false)
        {
            _isLoading = false;
            player_loading_indicator.SetVisible(false);
        }

        if (_isBuffering != false)
        {
            _isBuffering = false;
            _bufferingPercent = -1;
            player_buffering_indicator.SetVisible(false);
        }
    }
}