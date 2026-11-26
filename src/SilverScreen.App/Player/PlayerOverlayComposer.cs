using Gtk;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player.RichText;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player;

/// <summary>
///     Composes and mounts the 11 player overlay child views onto the root Gtk.Overlay.
/// </summary>
internal sealed class PlayerOverlayComposer : IDisposable
{
    private readonly PlayerDependencies _dependencies;
    private readonly DisposeScope _lifetime = new();

    public PlayerOverlayComposer(
        Overlay overlay,
        Widget rootWidget,
        LibMpvPlayer player,
        PlaybackSession session,
        INavigationService navigation,
        IPreferencesService preferences,
        PlayerDependencies dependencies,
        PlayerLinkRouter linkRouter,
        Action returnToShell,
        Func<bool> toggleFullscreen,
        Action closeComments,
        Action openComments,
        Func<bool> isCommentsOpen)
    {
        _dependencies = dependencies;
        Surface = _lifetime.Own(new PlayerSurfaceView());
        Surface.AttachPlayer(player);

        Loading = _lifetime.Own(new PlayerLoadingView());
        Loading.AttachPlayer(player);

        Error = _lifetime.Own(new PlayerErrorView());
        HeaderBar = _lifetime.Own(new PlayerHeaderBarView());
        CenterControls = _lifetime.Own(new PlayerCenterControlsView());

        Reactions = _lifetime.Own(new PlayerReactionsView());
        Reactions.AttachSession(session);

        Timeline = _lifetime.Own(new PlayerTimelineView());
        Timeline.Attach(player, () => Chrome?.RegisterActivity());

        ControlsBar = _lifetime.Own(new PlayerControlsBarView(navigation, Timeline, Reactions));
        ControlsBar.AttachSubtitleController(preferences, player.SelectSubtitleTrack);

        Stats = _lifetime.Own(new PlayerStatsView());
        Stats.AttachPlayer(player);

        InfoPanel = _lifetime.Own(new PlayerInfoPanelView(
            dependencies.MediaResolver,
            navigation,
            linkRouter.Activate,
            () =>
            {
                if (session.HasMedia) Surface.GrabFocus();
            }));

        CommentsCue = _lifetime.Own(new PlayerCommentsCueView());

        PromptStack = _lifetime.Own(new PlayerPromptStackView());
        PromptStack.AttachControllers(session, preferences, Timeline.TimelineScale, Timeline.TimelineOverlay);

        Osd = _lifetime.Own(new PlayerOsdView());
        Osd.AttachPreferences(preferences);

        // Wire UI control events to player transport
        HeaderBar.BackRequested += (_, _) => returnToShell();
        HeaderBar.ToggleFullscreenRequested += (_, _) => toggleFullscreen();
        CenterControls.PlayPauseRequested += (_, _) => player.TogglePause();
        CenterControls.RewindRequested += (_, _) => player.SeekRelative(-10);
        CenterControls.ForwardRequested += (_, _) => player.SeekRelative(10);
        ControlsBar.VolumeChanged += (_, vol) => player.SetVolume(vol);
        ControlsBar.SpeedChanged += (_, spd) => player.SetSpeed(spd);
        ControlsBar.QualitySelected += (_, q) => player.SetQuality(q);
        ControlsBar.PreviousQueueClicked += (_, _) => player.MovePlaylist(false);
        ControlsBar.NextQueueClicked += (_, _) => player.MovePlaylist(true);
        ControlsBar.SubtitleToggled += (_, sub) =>
        {
            if (!string.IsNullOrEmpty(sub))
                Osd.ShowSubtitles(sub);
        };
        CommentsCue.CueClicked += (_, _) => openComments();

        PromptStack.SeekRequested += (_, pos) =>
        {
            Timeline.SeekAbsolute(pos.TotalSeconds);
            Osd.ShowSeek((int)pos.TotalSeconds);
        };

        _lifetime.Track(
            () => session.SeekRequested += OnSessionSeekRequested,
            () => session.SeekRequested -= OnSessionSeekRequested);

        // Mount layers in exact visual stacking order
        overlay.Child = Surface.Widget;
        overlay.AddOverlay(Loading.Widget);
        overlay.AddOverlay(Error.Widget);
        overlay.AddOverlay(HeaderBar.Widget);
        overlay.AddOverlay(CenterControls.Widget);
        overlay.AddOverlay(ControlsBar.Widget);
        overlay.AddOverlay(Stats.Widget);
        overlay.AddOverlay(InfoPanel.Widget);
        overlay.AddOverlay(InfoPanel.CueWidget);
        overlay.AddOverlay(CommentsCue.Widget);
        overlay.AddOverlay(PromptStack.Widget);
        overlay.AddOverlay(Osd.Widget);

        Chrome = _lifetime.Own(new PlayerChromeController(
            rootWidget,
            HeaderBar.Widget,
            CenterControls.Widget,
            ControlsBar.Widget,
            () => ControlsBar.IsPopoverOpen || InfoPanel.IsOpen,
            () => Timeline.LayoutChapters(),
            (x, y) =>
            {
                var h = rootWidget.GetAllocatedHeight();
                var w = rootWidget.GetAllocatedWidth();
                InfoPanel.UpdatePointer(x, y, w, h, session.HasMedia);
                CommentsCue.UpdatePointer(x, y, w, h, isCommentsOpen(), session.HasMedia);
            },
            visible =>
            {
                Osd.SetChromeVisible(visible);
                if (visible && LastState is { } st)
                {
                    Timeline.UpdatePosition(st);
                    Timeline.UpdateChapters(st.Chapters, st.Duration);
                }
            }));

        Shortcuts = _lifetime.Own(new PlayerShortcutController(rootWidget));
        PlayerShortcutWiring.Wire(
            Shortcuts,
            this,
            player,
            session,
            returnToShell,
            toggleFullscreen,
            closeComments,
            isCommentsOpen,
            preferences);
    }

    public PlayerSurfaceView Surface { get; }
    public PlayerLoadingView Loading { get; }
    public PlayerErrorView Error { get; }
    public PlayerHeaderBarView HeaderBar { get; }
    private PlayerCenterControlsView CenterControls { get; }
    private PlayerReactionsView Reactions { get; }
    public PlayerTimelineView Timeline { get; }
    public PlayerControlsBarView ControlsBar { get; }
    public PlayerStatsView Stats { get; }
    public PlayerInfoPanelView InfoPanel { get; }
    public PlayerCommentsCueView CommentsCue { get; }
    public PlayerPromptStackView PromptStack { get; }
    public PlayerOsdView Osd { get; }
    public PlayerChromeController Chrome { get; }
    public PlayerShortcutController Shortcuts { get; }

    private LibMpvPlaybackState? LastState { get; set; }
    public bool IsPaused => LastState?.IsPaused ?? true;
    public double Volume => LastState?.Volume ?? 100.0;
    public bool IsMuted => LastState?.IsMuted ?? false;
    public double Speed => LastState?.Speed ?? 1.0;

    public void Dispose()
    {
        _lifetime.Dispose();
    }

    public void SyncState(LibMpvPlaybackState state, bool hasQueue, bool canPrev, bool canNext, bool isQueueOpen)
    {
        LastState = state;
        var chromeVisible = Chrome.ControlsVisible;
        CenterControls.SetPlaybackState(state is { HasMedia: true, IsPaused: false });
        CenterControls.SetLoading(state.IsLoading);
        Timeline.UpdatePosition(state, chromeVisible);
        Timeline.UpdateChapters(state.Chapters, state.Duration, chromeVisible);
        ControlsBar.SetVolume(state.Volume);
        ControlsBar.SetSpeed(state.Speed);
        ControlsBar.UpdateSubtitleTracks(state.SubtitleTracks, true);
        if (state.IsLoading) Loading.SetLoading(true);
        else if (state.IsBuffering) Loading.SetBuffering(true, state.BufferingPercent);
        else Loading.Reset();
        ControlsBar.SetQueueControls(hasQueue, canPrev, canNext, isQueueOpen);
    }

    public void SyncSessionVideo(VideoSummary video, string targetQuality)
    {
        InfoPanel.SetVideo(video);
        ControlsBar.SetMetadata(video.Title, video.ChannelName, video.ChannelUrl);
        Timeline.SetExpectedMedia(video.Duration, video.Duration <= TimeSpan.Zero);
        ControlsBar.LoadQualityTracks(video.Id, _dependencies.MediaResolver, targetQuality);
    }

    public void SyncSessionFailed(string detail, VideoSummary? failedVideo)
    {
        Loading.Reset();
        CenterControls.SetLoading(false);
        Error.ShowError(detail, failedVideo);
        ControlsBar.SetMetadata(Error.CurrentErrorInfo?.Title ?? "Playback Error",
            Error.CurrentErrorInfo?.Message ?? detail, null);
    }

    public void SyncSessionEnded()
    {
        Timeline.Reset();
        Osd.HideImmediate();
        Stats.Close();
        Error.ClearError();
        Loading.Reset();
        CenterControls.SetLoading(false);
        InfoPanel.SetVideo(null);
        ControlsBar.SetQueueControls(false, false, false, false);
        CommentsCue.Hide();
    }

    private void OnSessionSeekRequested(double positionSeconds, bool exact)
    {
        Timeline.SeekAbsolute(positionSeconds, exact);
    }
}