using Gtk;
using Functions = GLib.Functions;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerChromeController : IDisposable
{
    private const long ControlsIdleDelayMilliseconds = 1_500;
    private const uint ControlsVisibilityCheckMilliseconds = 100;
    private readonly Box _centerControls;
    private readonly GestureClick _clickGesture;

    private readonly Widget _headerBar;

    private readonly EventControllerMotion _motionController;
    private readonly Action? _onActivity;
    private readonly Action<bool>? _onControlsVisibilityChanged;
    private readonly Action<double, double>? _onPointerMoved;
    private readonly Widget _playerControls;

    private readonly Widget _viewWidget;

    private bool _disposed;
    private long _lastActivityMilliseconds;
    private double _lastPointerX = double.NaN;
    private double _lastPointerY = double.NaN;
    private uint _timeoutSource;

    public PlayerChromeController(
        Widget viewWidget,
        Widget headerBar,
        Box centerControls,
        Widget playerControls,
        Func<bool> hasOpenPopover,
        Action? onActivity = null,
        Action<double, double>? onPointerMoved = null,
        Action<bool>? onControlsVisibilityChanged = null)
    {
        _viewWidget = viewWidget;
        _headerBar = headerBar;
        _centerControls = centerControls;
        _playerControls = playerControls;

        _onActivity = onActivity;
        _onPointerMoved = onPointerMoved;
        _onControlsVisibilityChanged = onControlsVisibilityChanged;
        _motionController = EventControllerMotion.New();
        _motionController.SetPropagationPhase(PropagationPhase.Capture);
        _motionController.OnMotion += OnMotion;
        _viewWidget.AddController(_motionController);

        _clickGesture = GestureClick.New();
        _clickGesture.Button = 0;
        _clickGesture.SetPropagationPhase(PropagationPhase.Capture);
        _clickGesture.OnPressed += OnPressed;
        _viewWidget.AddController(_clickGesture);

        RegisterActivity();

        _timeoutSource = Functions.TimeoutAdd(0, ControlsVisibilityCheckMilliseconds, () =>
        {
            if (_disposed) return false;
            if (ControlsVisible &&
                !hasOpenPopover() &&
                Environment.TickCount64 - _lastActivityMilliseconds >= ControlsIdleDelayMilliseconds)
                SetControlsVisible(false);

            return true;
        });
    }

    private bool ControlsVisible { get; set; } = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_timeoutSource != 0)
        {
            Functions.SourceRemove(_timeoutSource);
            _timeoutSource = 0;
        }

        _motionController.OnMotion -= OnMotion;
        _viewWidget.RemoveController(_motionController);
        _motionController.Dispose();

        _clickGesture.OnPressed -= OnPressed;
        _viewWidget.RemoveController(_clickGesture);
        _clickGesture.Dispose();
    }

    public void RegisterActivity()
    {
        if (_disposed) return;
        _lastActivityMilliseconds = Environment.TickCount64;
        _onActivity?.Invoke();
        SetControlsVisible(true);
    }

    private void RegisterPointerActivity(double x, double y)
    {
        if (_disposed) return;
        if (Math.Abs(x - _lastPointerX) < 0.2 && Math.Abs(y - _lastPointerY) < 0.2) return;
        _lastPointerX = x;
        _lastPointerY = y;
        RegisterActivity();
        _onPointerMoved?.Invoke(x, y);
    }

    private void SetControlsVisible(bool visible)
    {
        if (ControlsVisible == visible) return;
        ControlsVisible = visible;
        SetControlVisible(_headerBar, visible);
        SetControlVisible(_centerControls, visible);
        SetControlVisible(_playerControls, visible);
        _onControlsVisibilityChanged?.Invoke(visible);
        if (!visible) _viewWidget.GrabFocus();
    }

    private static void SetControlVisible(Widget control, bool visible)
    {
        control.SetSensitive(visible);
        if (visible)
            control.RemoveCssClass("player-chrome-hidden");
        else
            control.AddCssClass("player-chrome-hidden");
    }

    private void OnMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        RegisterPointerActivity(args.X, args.Y);
    }

    private void OnPressed(GestureClick sender, GestureClick.PressedSignalArgs args)
    {
        RegisterActivity();
    }
}