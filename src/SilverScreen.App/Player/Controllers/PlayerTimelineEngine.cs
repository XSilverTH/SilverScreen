namespace SilverScreen.Player.Controllers;

/// <summary>
///     Wave 1 compatibility shim (NOT marked [Obsolete]: the test suite treats
///     warnings as errors and still exercises this name). Timeline state is owned by
///     <see cref="PlayerTimelineController" /> via <see cref="PlayerTimelineState" />;
///     this type forwards everything to the state base and will be removed in Wave 2.
/// </summary>
public sealed class PlayerTimelineEngine(
    uint seekThrottleIntervalMs = PlayerTimelineState.DefaultSeekThrottleIntervalMilliseconds,
    long reconciliationLatchMs = PlayerTimelineState.DefaultReconciliationLatchMilliseconds,
    double seekToleranceSeconds = PlayerTimelineState.DefaultSeekReconciliationToleranceSeconds,
    Func<long>? tickCountProvider = null)
    : PlayerTimelineState(seekThrottleIntervalMs, reconciliationLatchMs, seekToleranceSeconds, tickCountProvider)
{
}
