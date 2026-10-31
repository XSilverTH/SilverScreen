namespace SilverScreen.Account.Auth;

internal sealed class WebLoginCaptureCoordinator : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _debounceDelay;
    private readonly Lock _gate = new();
    private readonly AsyncLocal<bool> _isDraining = new();

    private readonly Func<string, Task<bool>> _persist;
    private readonly Action _persisted;
    private readonly Action _persistenceFailed;
    private readonly Action<Exception> _readFailed;
    private readonly Func<Task<string?>> _readReadyCookies;

    private bool _captureRequested;
    private Task _drainTask = Task.CompletedTask;
    private bool _stopped;

    internal WebLoginCaptureCoordinator(
        Func<Task<string?>> readReadyCookies,
        Func<string, Task<bool>> persist,
        Action persisted,
        Action<Exception> readFailed,
        Action persistenceFailed,
        TimeSpan? debounceDelay = null)
    {
        _readReadyCookies = readReadyCookies;
        _persist = persist;
        _persisted = persisted;
        _readFailed = readFailed;
        _persistenceFailed = persistenceFailed;
        _debounceDelay = debounceDelay ?? DefaultDebounceDelay;
    }

    internal WebLoginCaptureCoordinator(
        Func<Task<string?>> readReadyCookies,
        Func<string, bool> persist,
        Action persisted,
        Action<Exception> readFailed,
        Action persistenceFailed,
        TimeSpan? debounceDelay = null)
        : this(readReadyCookies, text => Task.FromResult(persist(text)), persisted, readFailed, persistenceFailed,
            debounceDelay)
    {
    }

    public void Dispose()
    {
        _cts.Dispose();
    }

    internal void RequestCapture()
    {
        lock (_gate)
        {
            if (_stopped)
                return;

            _captureRequested = true;
            if (_drainTask.IsCompleted)
                _drainTask = DrainAsync();
        }
    }

    internal async Task StopAsync()
    {
        Task drainTask;
        lock (_gate)
        {
            _stopped = true;
            _captureRequested = false;
            drainTask = _drainTask;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        if (_isDraining.Value)
            return;

        try
        {
            if (drainTask != Task.CompletedTask && !drainTask.IsCompleted)
            {
                await drainTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainAsync()
    {
        _isDraining.Value = true;
        while (true)
        {
            lock (_gate)
            {
                if (_stopped || !_captureRequested)
                    return;

                _captureRequested = false;
            }

            if (_debounceDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(_debounceDelay, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                lock (_gate)
                {
                    if (_stopped)
                        return;

                    if (_captureRequested)
                        // Another capture request arrived during debounce; restart window to let cookies settle
                        continue;
                }
            }

            string? cookieText;
            try
            {
                cookieText = await _readReadyCookies().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                bool shouldReport;
                lock (_gate)
                {
                    shouldReport = !_stopped;
                }

                if (shouldReport)
                    _readFailed(exception);
                continue;
            }

            lock (_gate)
            {
                if (_stopped)
                    return;

                if (cookieText is null)
                    continue;
            }

            if (!await _persist(cookieText).ConfigureAwait(false))
            {
                bool shouldReport;
                lock (_gate)
                {
                    shouldReport = !_stopped;
                }

                if (shouldReport)
                    _persistenceFailed();
                continue;
            }

            lock (_gate)
            {
                if (_stopped)
                    return;

                _stopped = true;
                _captureRequested = false;
            }

            _persisted();
            return;
        }
    }
}