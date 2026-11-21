using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Player.Controllers;
using SilverScreen.Shell;
using XSTH.Blueprint.Helpers;
using Object = GObject.Object;

namespace SilverScreen.Player.Views;

/// <summary>
///     Encapsulates the video info details slide-up panel, backdrop dismissal,
///     and bottom-edge hover cue. Replaces VideoInfoPanelController.
/// </summary>
public partial class PlayerInfoPanelView : ViewBase<Overlay>
{
    private static readonly ILogger Logger = Log.ForContext<PlayerInfoPanelView>();

    private readonly PlayerVideoInfoLoader _loader;
    private readonly INavigationService _navigation;
    private readonly Func<string, bool> _linkActivated;
    private readonly Action? _closed;
    private bool _bottomEdgeActive;
    private VideoSummary? _currentVideo;

    public bool IsOpen { get; private set; }

    public PlayerInfoPanelView(
        IYouTubeMediaResolver mediaResolver,
        INavigationService navigation,
        Func<string, bool> linkActivated,
        Action? closed = null)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _linkActivated = linkActivated ?? throw new ArgumentNullException(nameof(linkActivated));
        _closed = closed;
        _loader = Lifetime.Own(new PlayerVideoInfoLoader(mediaResolver));

        player_info_backdrop.SetVisible(false);
        player_info_revealer.SetVisible(false);
        player_info_revealer.RevealChild = false;
        player_info_cue_revealer.SetVisible(false);
        player_info_cue_revealer.RevealChild = false;
        Widget.CanTarget = false;

        SetInfoContent(null);
    }

    public void Show(VideoSummary video)
    {
        if (IsDisposed) return;
        _currentVideo = video;
        IsOpen = true;
        _bottomEdgeActive = false;
        player_info_cue_revealer.RevealChild = false;
        player_info_cue_revealer.SetVisible(false);
        player_info_backdrop.SetVisible(true);
        player_info_revealer.SetVisible(true);
        player_info_revealer.RevealChild = true;
        Widget.CanTarget = true;
        player_info_title_label.SetText(video.Title);
        player_info_channel_label.SetText(video.ChannelName);
        player_info_close_button.GrabFocus();

        if (_loader.LoadedDetails is { } details) SetInfoContent(details);
        else if (_loader.LoadedError is { } error) ShowError(error);
        else
        {
            player_info_status_label.SetText("Loading video details…");
            player_info_status_label.SetVisible(true);
            player_info_description_scroller.SetVisible(false);
            _loader.LoadAsync(video.Id, () =>
            {
                if (!IsOpen) return;
                if (_loader.LoadedDetails is { } d) SetInfoContent(d);
                else if (_loader.LoadedError is { } e) ShowError(e);
            }).FireAndForget();
        }
    }

    public void Close()
    {
        if (!IsOpen && !player_info_revealer.RevealChild) return;
        IsOpen = false;
        player_info_revealer.RevealChild = false;
        if (!player_info_revealer.ChildRevealed) player_info_revealer.SetVisible(false);
        player_info_backdrop.SetVisible(false);
        player_info_cue_revealer.RevealChild = false;
        if (!player_info_cue_revealer.ChildRevealed) player_info_cue_revealer.SetVisible(false);
        _bottomEdgeActive = false;
        Widget.CanTarget = false;
        SetInfoContent(null);
        _closed?.Invoke();
    }

    public void Toggle(VideoSummary? video = null)
    {
        if (IsOpen)
            Close();
        else if ((video ?? _currentVideo) is { } targetVideo)
            Show(targetVideo);
    }

    public void SetVideo(VideoSummary? video)
    {
        if (string.Equals(_currentVideo?.Id, video?.Id, StringComparison.Ordinal)) return;
        if (IsOpen) Close();
        _loader.Reset();
        _currentVideo = video;
        if (!IsOpen) SetInfoContent(null);
        if (video is not null)
            _loader.LoadAsync(video.Id, () => { if (IsOpen && _loader.LoadedDetails is { } d) SetInfoContent(d); }).FireAndForget();
    }

    public void UpdatePointer(double x, double y, double width, double height, bool hasMedia)
    {
        if (!hasMedia || IsOpen || width <= 0 || height <= 0)
        {
            if (!_bottomEdgeActive) return;
            _bottomEdgeActive = false;
            player_info_cue_revealer.RevealChild = false;
            Widget.CanTarget = IsOpen;
            return;
        }

        var inZone = PlayerCueGeometry.IsInfoCueActive(x, y, width, height, _bottomEdgeActive);
        if (_bottomEdgeActive == inZone) return;
        _bottomEdgeActive = inZone;
        if (inZone) player_info_cue_revealer.SetVisible(true);
        player_info_cue_revealer.RevealChild = inZone;
        Widget.CanTarget = inZone;
    }

    private void OnBackdropClicked(object? sender, EventArgs args) => Close();
    private void OnCloseButtonClicked(object? sender, EventArgs args) => Close();
    private void OnCueButtonClicked(object? sender, EventArgs args) { if (_currentVideo is { } v) Show(v); }
    private bool OnDescriptionLinkActivated(Label sender, Label.ActivateLinkSignalArgs args) => _linkActivated(args.Uri);

    private void OnChannelButtonClicked(object? sender, EventArgs args)
    {
        if (_currentVideo is not { } video) return;
        Close();
        if (!string.IsNullOrWhiteSpace(video.ChannelUrl))
            _navigation.NavigateToChannel(video.ChannelUrl, video.ChannelName);
    }

    private void OnRevealerNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "child-revealed" && player_info_revealer is { ChildRevealed: false, RevealChild: false })
            player_info_revealer.SetVisible(false);
    }

    private void OnCueRevealerNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "child-revealed" && player_info_cue_revealer is { ChildRevealed: false, RevealChild: false })
            player_info_cue_revealer.SetVisible(false);
    }

    private void ShowError(string error)
    {
        player_info_status_label.SetText(error);
        player_info_status_label.SetVisible(true);
        player_info_description_scroller.SetVisible(false);
    }

    private void SetInfoContent(YouTubeVideoDetails? details)
    {
        player_info_title_label.SetText(details?.Title ?? _currentVideo?.Title ?? "Video details");
        player_info_channel_label.SetText(details?.ChannelName ?? _currentVideo?.ChannelName ?? string.Empty);
        player_info_stats_label.SetText(details is not null ? PlayerVideoInfoLoader.FormatStats(details) : string.Empty);
        player_info_status_label.SetText(details is null ? "Move to the bottom edge to reveal video details." : "This video has no description.");
        player_info_status_label.SetVisible(details is null || string.IsNullOrWhiteSpace(details.Description));
        player_info_description_scroller.SetVisible(details is not null && !string.IsNullOrWhiteSpace(details.Description));
        if (details is not null)
        {
            var description = details.Description ?? string.Empty;
            try
            {
                player_info_description.SetMarkup(PlayerVideoInfoLoader.FormatMarkup(description));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to render video description markup; falling back to plain text");
                player_info_description.SetText(description);
            }
        }
    }
}
