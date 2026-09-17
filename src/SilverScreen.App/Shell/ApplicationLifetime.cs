namespace SilverScreen.Shell;

/// <summary>Tracks the single application window and presents it on repeated activation.</summary>
internal sealed class ActivationWindowGuard<TWindow>
    where TWindow : class
{
    private TWindow? _window;

    public bool TryPresentExisting(Action<TWindow> present)
    {
        ArgumentNullException.ThrowIfNull(present);

        var window = _window;
        if (window is null)
            return false;

        present(window);
        return true;
    }

    public void Set(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    public void Clear(TWindow window)
    {
        if (ReferenceEquals(_window, window))
            _window = null;
    }
}

/// <summary>Disposes shared services when the application or its last window closes.</summary>
internal sealed class ApplicationServiceLifetime(Action dispose)
{
    private readonly Action _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    private int _windowCount;
    private int _disposed;

    public void WindowOpened() => Interlocked.Increment(ref _windowCount);

    public void WindowClosed()
    {
        if (Interlocked.Decrement(ref _windowCount) <= 0)
            DisposeOnce();
    }

    public void ApplicationStopped() => DisposeOnce();

    private void DisposeOnce()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _dispose();
    }
}
