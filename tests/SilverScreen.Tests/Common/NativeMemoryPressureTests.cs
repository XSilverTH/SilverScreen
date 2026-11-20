using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

public sealed class NativeMemoryPressureTests
{
    [Fact]
    public void InitialState_TrackedBytesIsZero()
    {
        using var pressure = new NativeMemoryPressure();
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Track_AddsPressure_AndRelease_IsIdempotent()
    {
        using var pressure = new NativeMemoryPressure();

        pressure.Track(1024 * 1024);
        Assert.Equal(1024 * 1024, pressure.TrackedBytes);

        // Successive tracking should release old pressure before adding new
        pressure.Track(512 * 1024);
        Assert.Equal(512 * 1024, pressure.TrackedBytes);

        // Release should be idempotent
        pressure.Release();
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Release();
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Track_WithZeroOrNegative_Initially_DoesNotThrow()
    {
        using var pressure = new NativeMemoryPressure();

        pressure.Track(0);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(-100);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Release();
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Track_SubsequentlyCallingZero_ReleasesPressureAndResetsTrackedBytes()
    {
        using var pressure = new NativeMemoryPressure();

        pressure.Track(1024 * 1024);
        Assert.Equal(1024 * 1024, pressure.TrackedBytes);

        pressure.Track(0);
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Track_SubsequentlyCallingNegative_ReleasesPressureAndResetsTrackedBytes()
    {
        using var pressure = new NativeMemoryPressure();

        pressure.Track(512 * 1024);
        Assert.Equal(512 * 1024, pressure.TrackedBytes);

        pressure.Track(-1);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(256 * 1024);
        Assert.Equal(256 * 1024, pressure.TrackedBytes);

        pressure.Track(-500);
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Dispose_ReleasesAnyTrackedPressure()
    {
        var pressure = new NativeMemoryPressure();
        pressure.Track(4096);
        Assert.Equal(4096, pressure.TrackedBytes);

        pressure.Dispose();
        Assert.Equal(0, pressure.TrackedBytes);

        // Subsequent dispose call should be idempotent
        pressure.Dispose();
        Assert.Equal(0, pressure.TrackedBytes);
    }

    [Fact]
    public void Track_MultipleConsecutiveCalls_TransitionsCorrectly()
    {
        using var pressure = new NativeMemoryPressure();

        pressure.Track(1000);
        Assert.Equal(1000, pressure.TrackedBytes);

        pressure.Track(2000);
        Assert.Equal(2000, pressure.TrackedBytes);

        pressure.Track(0);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(0);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(-50);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(500);
        Assert.Equal(500, pressure.TrackedBytes);

        pressure.Track(-1);
        Assert.Equal(0, pressure.TrackedBytes);

        pressure.Track(100);
        Assert.Equal(100, pressure.TrackedBytes);

        pressure.Release();
        Assert.Equal(0, pressure.TrackedBytes);
    }
}