using Adw;
using Gtk;
using Pango;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;
using Context = Cairo.Context;
using Functions = PangoCairo.Functions;
using Object = GObject.Object;

namespace SilverScreen.Player.Status;

/// <summary>
///     Binds stream delay countdown and progress UI to libmpv events.
///     Renders a native progress capsule with dynamic contrast text clipping.
/// </summary>
internal sealed class PlayerStreamDelayController : IDisposable
{
    private const uint TickIntervalMilliseconds = 33; // ~30 FPS
    private readonly DrawingArea _drawingArea;
    private readonly FontDescription _fontDescription;
    private readonly DisposeScope _lifetime = new();
    private readonly Revealer _revealer;
    private readonly PlayerStreamDelayState _state;
    private double _accentA = 1.0;
    private double _accentB = 0.89;
    private double _accentG = 0.52;
    private bool _accentIsLight;
    private double _accentR = 0.21;
    private double _currentFraction;
    private string _currentLabelText = string.Empty;
    private bool _hasCachedAccent;
    private uint _tickSource;

    public PlayerStreamDelayController(
        LibMpvPlayer player,
        Revealer revealer,
        DrawingArea drawingArea,
        PlayerStreamDelayState? state = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        _revealer = revealer ?? throw new ArgumentNullException(nameof(revealer));
        _drawingArea = drawingArea ?? throw new ArgumentNullException(nameof(drawingArea));
        _state = state ?? new PlayerStreamDelayState();
        _fontDescription = FontDescription.FromString("Sans Bold 9");

        _revealer.SetVisible(false);
        _revealer.RevealChild = false;

        _drawingArea.SetDrawFunc(DrawDelayBar);

        _lifetime.Track(
            () => _revealer.OnNotify += OnRevealerNotify,
            () => _revealer.OnNotify -= OnRevealerNotify);

        _lifetime.Track(
            () => player.StreamDelayDetected += OnStreamDelayDetected,
            () => player.StreamDelayDetected -= OnStreamDelayDetected);
    }

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        CancelTimer();
        _lifetime.Dispose();
        _fontDescription.Dispose();
        _state.Reset();
    }

    public void Reset()
    {
        var hadActivity = _state.IsActive || _tickSource != 0 || _revealer.RevealChild || _revealer.GetVisible() ||
                          !string.IsNullOrEmpty(_currentLabelText);
        CancelTimer();
        _state.Reset();
        if (!hadActivity) return;
        _currentFraction = 0.0;
        _currentLabelText = string.Empty;
        _revealer.RevealChild = false;
        _revealer.SetVisible(false);
        _drawingArea.QueueDraw();
    }

    private void OnRevealerNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "child-revealed" && _revealer is { ChildRevealed: false, RevealChild: false })
        {
            CancelTimer();
            _revealer.SetVisible(false);
        }
    }

    private void OnStreamDelayDetected(object? sender, TimeSpan delay)
    {
        GtkUiDispatch.Run(() => !_lifetime.IsDisposed, () => StartDelay(delay));
    }

    private void StartDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) return;

        CancelTimer();
        _hasCachedAccent = false;
        _state.Start(delay);

        _currentFraction = 0.0;
        _currentLabelText = PlayerStreamDelayState.FormatLabel(delay.TotalSeconds);

        _revealer.SetVisible(true);
        _revealer.RevealChild = true;
        _drawingArea.QueueDraw();

        _tickSource = _lifetime.Timeout(TickIntervalMilliseconds, OnTick);
    }

    private bool OnTick()
    {
        if (_lifetime.IsDisposed || !_state.IsActive || !_revealer.GetVisible() || !_revealer.RevealChild)
        {
            _tickSource = 0;
            return false;
        }

        var (fraction, remainingSeconds, isComplete) = _state.Update();

        _currentFraction = fraction;
        _currentLabelText = PlayerStreamDelayState.FormatLabel(remainingSeconds);
        _drawingArea.QueueDraw();

        if (!isComplete) return true;
        _tickSource = 0;
        _revealer.RevealChild = false;
        return false;
    }

    private void CancelTimer()
    {
        if (_tickSource == 0) return;
        _lifetime.Cancel(_tickSource);
        _tickSource = 0;
    }

    private void EnsureAccentColor(DrawingArea area)
    {
        if (_hasCachedAccent) return;

        double fillR = 0.21, fillG = 0.52, fillB = 0.89, fillA = 1.0;
        var hasBg = area.GetStyleContext().LookupColor("accent_bg_color", out var accentBg);
        if (hasBg)
        {
            fillR = accentBg.Red;
            fillG = accentBg.Green;
            fillB = accentBg.Blue;
            fillA = accentBg.Alpha;
        }
        else
        {
            try
            {
                var adwRgba = StyleManager.GetDefault().AccentColorRgba;
                fillR = adwRgba.Red;
                fillG = adwRgba.Green;
                fillB = adwRgba.Blue;
                fillA = adwRgba.Alpha;
            }
            catch
            {
                // default fallback
            }
        }

        _accentR = fillR;
        _accentG = fillG;
        _accentB = fillB;
        _accentA = fillA;
        _accentIsLight = IsLightColor(fillR, fillG, fillB);
        _hasCachedAccent = true;
    }

    private void DrawDelayBar(DrawingArea area, Context cr, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        var radius = height / 2.0;

        // 1. Trough background and border
        PathPill();
        cr.SetSourceRgba(0.09, 0.09, 0.09, 0.95);
        cr.FillPreserve();
        cr.SetSourceRgba(1.0, 1.0, 1.0, 0.15);
        cr.LineWidth = 1.0;
        cr.Stroke();

        // 2. Progress fill
        EnsureAccentColor(area);
        var fillWidth = width * Math.Clamp(_currentFraction, 0.0, 1.0);

        if (fillWidth > 0)
        {
            cr.Save();
            PathPill();
            cr.Clip();
            cr.Rectangle(0, 0, fillWidth, height);
            cr.SetSourceRgba(_accentR, _accentG, _accentB, _accentA);
            cr.Fill();
            cr.Restore();
        }

        // 3. Text layout with split-contrast clipping
        var text = _currentLabelText;
        if (string.IsNullOrEmpty(text)) return;

        using var layout = area.CreatePangoLayout(text);
        layout.SetFontDescription(_fontDescription);
        layout.GetPixelSize(out var textWidth, out var textHeight);
        var textX = (width - textWidth) / 2.0;
        var textY = (height - textHeight) / 2.0;

        // Unfilled region (right of fillWidth): trough is dark, so text is crisp white
        cr.Save();
        PathPill();
        cr.Clip();
        cr.Rectangle(fillWidth, 0, width - fillWidth, height);
        cr.Clip();
        cr.SetSourceRgba(1.0, 1.0, 1.0, 0.95);
        cr.MoveTo(textX, textY);
        Functions.ShowLayout(cr, layout);
        cr.Restore();

        // Filled region (left of fillWidth): black on light colors, white on dark colors
        if (!(fillWidth > 0)) return;
        cr.Save();
        PathPill();
        cr.Clip();
        cr.Rectangle(0, 0, fillWidth, height);
        cr.Clip();

        if (_accentIsLight)
            cr.SetSourceRgba(0.0, 0.0, 0.0, 0.95);
        else
            cr.SetSourceRgba(1.0, 1.0, 1.0, 0.95);

        cr.MoveTo(textX, textY);
        Functions.ShowLayout(cr, layout);
        cr.Restore();
        return;

        void PathPill()
        {
            cr.NewSubPath();
            cr.Arc(width - radius, radius, radius, -Math.PI / 2.0, Math.PI / 2.0);
            cr.Arc(radius, radius, radius, Math.PI / 2.0, 3 * Math.PI / 2.0);
            cr.ClosePath();
        }
    }

    /// <summary>
    ///     Calculates whether a color has high relative luminance (WCAG 2.0 definition).
    ///     Returns true for light backgrounds (requiring black text), false for dark backgrounds (requiring white text).
    /// </summary>
    internal static bool IsLightColor(double r, double g, double b)
    {
        var luminance = 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);
        return luminance > 0.40;

        static double ToLinear(double c)
        {
            var clamped = Math.Clamp(c, 0.0, 1.0);
            return clamped <= 0.04045 ? clamped / 12.92 : Math.Pow((clamped + 0.055) / 1.055, 2.4);
        }
    }
}