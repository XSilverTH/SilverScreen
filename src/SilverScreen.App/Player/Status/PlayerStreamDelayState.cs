using System.Diagnostics;
using System.Globalization;

namespace SilverScreen.Player.Status;

/// <summary>
///     Pure state tracking and progress calculation for stream delay countdowns.
/// </summary>
public sealed class PlayerStreamDelayState(Func<long>? timestampProvider = null)
{
    private readonly Func<long> _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
    private long _startTimestamp;
    private TimeSpan _totalDelay;

    public bool IsActive { get; private set; }
    public TimeSpan TotalDelay => _totalDelay;

    public void Start(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            Reset();
            return;
        }

        _totalDelay = delay;
        _startTimestamp = _timestampProvider();
        IsActive = true;
    }

    public void Reset()
    {
        IsActive = false;
        _totalDelay = TimeSpan.Zero;
        _startTimestamp = 0;
    }

    public (double Fraction, double RemainingSeconds, bool IsComplete) Update()
    {
        if (!IsActive || _totalDelay <= TimeSpan.Zero)
            return (1.0, 0.0, true);

        var currentTimestamp = _timestampProvider();
        var elapsedTicks = currentTimestamp - _startTimestamp;
        var elapsedSeconds = Math.Max(0.0, (double)elapsedTicks / Stopwatch.Frequency);
        var totalSeconds = _totalDelay.TotalSeconds;

        if (elapsedSeconds >= totalSeconds)
        {
            IsActive = false;
            return (1.0, 0.0, true);
        }

        var fraction = Math.Clamp(elapsedSeconds / totalSeconds, 0.0, 1.0);
        var remainingSeconds = Math.Max(0.0, totalSeconds - elapsedSeconds);
        return (fraction, remainingSeconds, false);
    }

    public static string FormatLabel(double remainingSeconds)
    {
        return $"YouTube ad-block delay… {remainingSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
    }
}