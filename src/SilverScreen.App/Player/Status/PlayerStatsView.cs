using Gtk;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Status;

internal sealed class PlayerStatsProvider(LibMpvPlayer player) : IPlayerStatsProvider
{
    public PlaybackStats? GetPlaybackStats(bool includeTracks = false)
    {
        return player.GetPlaybackStats(includeTracks);
    }
}

/// <summary>
///     Encapsulates the "Stats for Nerds" technical telemetry revealer overlay.
/// </summary>
public partial class PlayerStatsView : ViewBase<Revealer>
{
    private PlayerStatsController? _statsController;

    public bool IsOpen => _statsController?.IsOpen ?? false;

    public void AttachPlayer(LibMpvPlayer player)
    {
        _statsController = Lifetime.Own(
            new PlayerStatsController(new PlayerStatsProvider(player), Widget, player_stats_label));
    }

    public bool HandleKeyPress(uint keyVal)
    {
        return _statsController?.HandleKeyPress(keyVal) ?? false;
    }

    public void Toggle()
    {
        _statsController?.Toggle();
    }

    public void Close()
    {
        _statsController?.Close();
    }
}