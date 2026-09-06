namespace SilverScreen.Account.Auth;

internal sealed class WebLoginCaptureCoordinator : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<string, bool> _persist;
    private readonly Action _persisted;
    private readonly Action _persistenceFailed;
    private readonly Action<Exception> _readFailed;
    private readonly Func<Task<string?>> _readReadyCookies;
    private readonly TimeSpan _debounceDelay;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private bool _captureRequested;
    private Task _drainTask = Task.CompletedTask;
    private bool _stopped;

    internal WebLoginCaptureCoordinator(
        Func<Task<string?>> readReadyCookies,
        Func<string, bool> persist,
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
        lock (_gate)
        {
            _stopped = true;
            _captureRequested = false;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainAsync()
    {
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
                    {
                        // Another capture request arrived during debounce; restart window to let cookies settle
                        continue;
                    }
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

            if (!_persist(cookieText))
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

    public void Dispose()
    {
        _cts.Dispose();
    }
}
