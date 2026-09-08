namespace SilverScreen.Player.Controllers;

/// <summary>
///     Wave 1 compatibility shim (NOT marked [Obsolete]: the test suite treats
///     warnings as errors and still exercises this name). OSD state is owned by
///     <see cref="PlayerOsdController" /> via <see cref="PlayerOsdState" />;
///     this type forwards everything to the state base and will be removed in Wave 2.
/// </summary>
public sealed class PlayerOsdEngine(
    uint aggregationWindowMilliseconds = PlayerOsdState.DefaultAggregationWindowMilliseconds,
    Func<long>? tickCountProvider = null)
    : PlayerOsdState(aggregationWindowMilliseconds, tickCountProvider)
{
}
