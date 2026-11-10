namespace SilverScreen.Core.Common;

/// <summary>
/// Bridges unmanaged pixel buffer allocations (such as GdkPixbuf and GdkTexture)
/// into the .NET Garbage Collector via GC.AddMemoryPressure and GC.RemoveMemoryPressure,
/// ensuring the GC is aware of native memory weight and schedules collections appropriately.
/// </summary>
public sealed class NativeMemoryPressure : IDisposable
{
    private long _trackedBytes;

    /// <summary>
    /// Informs the GC that unmanaged memory of the given size has been allocated.
    /// Releases any previously tracked bytes on this instance first.
    /// </summary>
    public void Track(long bytes)
    {
        if (bytes <= 0) return;
        Release();
        _trackedBytes = bytes;
        GC.AddMemoryPressure(bytes);
    }

    /// <summary>
    /// Informs the GC that the tracked unmanaged memory has been freed.
    /// Idempotent: safe to call repeatedly.
    /// </summary>
    public void Release()
    {
        var bytes = Interlocked.Exchange(ref _trackedBytes, 0);
        if (bytes > 0)
        {
            GC.RemoveMemoryPressure(bytes);
        }
    }

    public void Dispose()
    {
        Release();
    }
}
