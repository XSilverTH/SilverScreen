namespace SilverScreen.Features.Session;

internal sealed class WebLoginCaptureCoordinator
{
    private readonly Func<Task<string?>> _readReadyCookies;
    private readonly Func<string, bool> _persist;
    private readonly Action _persisted;
    private readonly Action<Exception> _readFailed;
    private readonly Action _persistenceFailed;
    private Task _drainTask = Task.CompletedTask;
    private bool _captureRequested;
    private bool _stopped;

    internal WebLoginCaptureCoordinator(
        Func<Task<string?>> readReadyCookies,
        Func<string, bool> persist,
        Action persisted,
        Action<Exception> readFailed,
        Action persistenceFailed)
    {
        _readReadyCookies = readReadyCookies;
        _persist = persist;
        _persisted = persisted;
        _readFailed = readFailed;
        _persistenceFailed = persistenceFailed;
    }

    internal void RequestCapture()
    {
        if (_stopped)
            return;

        _captureRequested = true;
        if (_drainTask.IsCompleted)
            _drainTask = DrainAsync();
    }

    internal Task StopAsync()
    {
        _stopped = true;
        _captureRequested = false;
        return _drainTask;
    }

    private async Task DrainAsync()
    {
        while (_captureRequested && !_stopped)
        {
            _captureRequested = false;
            string? cookieText;
            try
            {
                cookieText = await _readReadyCookies();
            }
            catch (Exception exception)
            {
                if (!_stopped)
                    _readFailed(exception);
                continue;
            }

            if (_stopped || cookieText is null)
                continue;

            if (!_persist(cookieText))
            {
                if (!_stopped)
                    _persistenceFailed();
                continue;
            }

            if (_stopped)
                return;

            _stopped = true;
            _captureRequested = false;
            _persisted();
        }
    }
}
