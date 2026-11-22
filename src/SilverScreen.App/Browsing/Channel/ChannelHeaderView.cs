using Gtk;
using SilverScreen.Core.Domain;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Channel;

public partial class ChannelHeaderView : ViewBase<Revealer, ChannelViewModel>
{
    private const double CollapseThreshold = 50.0, TopRevealThreshold = 1.0, TopHoverZoneThreshold = 40.0;
    private const long LayoutStabilizationMs = 350;
    private readonly ChannelControlsBarView _controlsBar;

    private readonly ChannelProfileCardView _profileCard;
    private bool _isHeaderCollapsed, _isUserScrollingUp;
    private long _lastHeaderStateChangeTicks, _lastScrollDownTicks, _lastUserScrollTicks;
    private double _lastScrollY;
    private Adjustment? _vadjustment;

    public ChannelHeaderView(IThumbnailService thumbnails, Func<int>? getBatchSize = null)
    {
        _profileCard = Lifetime.Own(new ChannelProfileCardView(thumbnails));
        _controlsBar = Lifetime.Own(new ChannelControlsBarView(getBatchSize));
        profile_host.Append(_profileCard.Widget);
        controls_host.Append(_controlsBar.Widget);
    }

    protected override void BindViewModel(ChannelViewModel viewModel, BindingScope<ChannelViewModel> bindings)
    {
        _profileCard.ViewModel = _controlsBar.ViewModel = viewModel;
    }

    public void AttachScrollTracking(ScrolledWindow scrolledWindow, Adjustment? vadjustment)
    {
        _vadjustment = vadjustment;
        if (vadjustment is not null)
            Lifetime.Track(
                () => vadjustment.OnValueChanged += OnScrollValueChanged,
                () => vadjustment.OnValueChanged -= OnScrollValueChanged);

        var scrollController = EventControllerScroll.New(EventControllerScrollFlags.Vertical);
        scrollController.SetPropagationPhase(PropagationPhase.Capture);
        Lifetime.Attach(scrolledWindow, scrollController,
            c => c.OnScroll += OnScrollEvent,
            c => c.OnScroll -= OnScrollEvent);

        var motionController = EventControllerMotion.New();
        motionController.SetPropagationPhase(PropagationPhase.Capture);
        Lifetime.Attach(scrolledWindow, motionController,
            c =>
            {
                c.OnEnter += (_, e) => CheckHoverReveal(e.Y);
                c.OnMotion += (_, e) => CheckHoverReveal(e.Y);
            },
            c =>
            {
                c.OnEnter -= (_, e) => CheckHoverReveal(e.Y);
                c.OnMotion -= (_, e) => CheckHoverReveal(e.Y);
            });
    }

    public void SetHeaderCollapsed(bool collapsed)
    {
        _isHeaderCollapsed = collapsed;
        Widget.RevealChild = !collapsed;
        _lastHeaderStateChangeTicks = Environment.TickCount64;
        if (_vadjustment is not null) _lastScrollY = _vadjustment.Value;
    }

    private bool OnScrollEvent(EventControllerScroll sender, EventControllerScroll.ScrollSignalArgs args)
    {
        if (IsDisposed) return false;
        switch (args.Dy)
        {
            case < -0.01:
                _isUserScrollingUp = true;
                _lastUserScrollTicks = Environment.TickCount64;
                break;
            case > 0.01:
                _isUserScrollingUp = false;
                _lastUserScrollTicks = _lastScrollDownTicks = Environment.TickCount64;
                break;
        }

        return false;
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || _vadjustment is null) return;
        var currentY = _vadjustment.Value;
        var now = Environment.TickCount64;
        if (now - _lastHeaderStateChangeTicks < LayoutStabilizationMs)
        {
            _lastScrollY = currentY;
            return;
        }

        if (!_isHeaderCollapsed)
        {
            if (currentY > CollapseThreshold && currentY > _lastScrollY) SetHeaderCollapsed(true);
        }
        else
        {
            var reachesTop = currentY <= TopRevealThreshold;
            var userMovingUp = currentY < _lastScrollY || (_isUserScrollingUp && now - _lastUserScrollTicks < 500);
            if (reachesTop && userMovingUp) SetHeaderCollapsed(false);
        }

        _lastScrollY = currentY;
    }

    private void CheckHoverReveal(double y)
    {
        if (IsDisposed || !_isHeaderCollapsed) return;
        var now = Environment.TickCount64;
        if (now - _lastHeaderStateChangeTicks < LayoutStabilizationMs || now - _lastScrollDownTicks < 300) return;
        if (y is >= 0 and <= TopHoverZoneThreshold) SetHeaderCollapsed(false);
    }
}