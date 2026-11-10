using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class NativeMemoryPressureTests
{
    [Fact]
    public void Track_AddsPressure_AndRelease_IsIdempotent()
    {
        using var pressure = new NativeMemoryPressure();

        // Should track without throwing
        pressure.Track(1024 * 1024);

        // Successive tracking should release old pressure before adding new
        pressure.Track(512 * 1024);

        // Release should be idempotent
        pressure.Release();
        pressure.Release();
    }

    [Fact]
    public void Track_WithZeroOrNegative_DoesNotThrow()
    {
        using var pressure = new NativeMemoryPressure();
        pressure.Track(0);
        pressure.Track(-100);
        pressure.Release();
    }
}
