using Gtk;
using XSTH.Blueprint.Helpers;
using Object = GObject.Object;

namespace SilverScreen.Player.Info;

/// <summary>
///     Encapsulates the side hover cue button revealed near the player's start edge to open comments.
/// </summary>
public partial class PlayerCommentsCueView : ViewBase<Revealer>
{
    private bool _leftEdgeActive;

    public event EventHandler? CueClicked;

    public void UpdatePointer(double x, double y, double width, double height, bool commentsOpen, bool hasMedia)
    {
        if (!hasMedia || commentsOpen || width <= 0 || height <= 0)
        {
            if (!_leftEdgeActive) return;
            _leftEdgeActive = false;
            Widget.RevealChild = false;
            return;
        }

        var inZone = PlayerCueGeometry.IsCommentsCueActive(x, y, width, height, _leftEdgeActive);
        if (_leftEdgeActive == inZone) return;
        _leftEdgeActive = inZone;
        if (inZone) Widget.SetVisible(true);
        Widget.RevealChild = inZone;
    }

    public void Hide()
    {
        _leftEdgeActive = false;
        Widget.RevealChild = false;
        Widget.SetVisible(false);
    }

    private void OnCueButtonClicked(object? sender, EventArgs args)
    {
        CueClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnRevealerNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "child-revealed" && Widget is { ChildRevealed: false, RevealChild: false })
            Widget.SetVisible(false);
    }
}
