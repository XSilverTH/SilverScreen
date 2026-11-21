using Gdk;
using Gtk;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;
using Constants = Gdk.Constants;

namespace SilverScreen.Browsing.Components;

public partial class VideoCardView : ViewBase<Adw.Bin>
{
    private readonly VideoCardThumbnailView _thumbnail;
    private readonly VideoCardMetadataView _metadata;
    private readonly VideoCardContextMenu _contextMenu;
    private VideoSummary? _video;

    public VideoCardView(VideoCardServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _thumbnail = Lifetime.Own(new VideoCardThumbnailView(services.Thumbnails));
        _metadata = Lifetime.Own(new VideoCardMetadataView(services.Navigation));
        _contextMenu = Lifetime.Own(new VideoCardContextMenu(services.Navigation, services.Playback, services.Queue, services.Notifications));

        card.Append(_thumbnail.Widget);
        card.Append(_metadata.Widget);
        _contextMenu.AttachTo(card, _metadata.MenuButton);
        _metadata.ContextMenuRequested += () => _contextMenu.ShowAt();

        var click = GestureClick.New(); click.Button = 0;
        Lifetime.Attach(card, click, c => c.OnReleased += OnCardReleased, c => c.OnReleased -= OnCardReleased);
        var rightClick = GestureClick.New(); rightClick.Button = 3;
        Lifetime.Attach(card, rightClick, c => c.OnPressed += OnCardRightClicked, c => c.OnPressed -= OnCardRightClicked);
        var keyController = EventControllerKey.New();
        Lifetime.Attach(card, keyController, c => c.OnKeyPressed += OnKeyPressed, c => c.OnKeyPressed -= OnKeyPressed);
    }

    public void Bind(VideoSummary video, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Unbind();
        _video = video;
        card.TooltipText = VideoCardFormatting.FormatCardTooltip(video);
        _thumbnail.Bind(video, ct);
        _metadata.Bind(video);
        _contextMenu.SetVideo(video);
    }

    public void Unbind()
    {
        _video = null; card.TooltipText = string.Empty;
        _contextMenu.Dismiss(); _contextMenu.SetVideo(null);
        _metadata.Unbind(); _thumbnail.Unbind();
    }

    private void OnCardReleased(GestureClick sender, GestureClick.ReleasedSignalArgs args)
    {
        if (_video is not { } v) return;
        if (sender.GetCurrentButton() == 1) _contextMenu.Play(v);
        else if (sender.GetCurrentButton() == 2) _contextMenu.AddToQueue(v);
    }

    private void OnCardRightClicked(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        if (_video is null) return;
        sender.SetState(EventSequenceState.Claimed);
        _contextMenu.ShowAt((int)args.X, (int)args.Y);
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (IsDisposed || _video is null) return false;
        if (args.Keyval is Constants.KEY_Return or Constants.KEY_KP_Enter or Constants.KEY_space) { _contextMenu.Play(_video); return true; }
        if (args.Keyval is Constants.KEY_Menu or Constants.KEY_F10 && (args.State & ModifierType.ShiftMask) != 0) { _contextMenu.ShowAt(); return true; }
        if (args.Keyval is Constants.KEY_c or Constants.KEY_C) { _contextMenu.OpenChannel(_video); return true; }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Unbind();
        base.Dispose(disposing);
    }
}
