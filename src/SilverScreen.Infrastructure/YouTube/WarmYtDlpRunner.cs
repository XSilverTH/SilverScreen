using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class WarmYtDlpRunner : IYtDlpRunner, IAsyncDisposable, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<WarmYtDlpRunner>();
    private readonly IYtDlpRunner _fallbackRunner;

    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpProcessHost _processHost;
    private bool _disposed;

    public WarmYtDlpRunner(
        IPreferencesService preferencesService,
        IYtDlpProcessHost processHost,
        IYtDlpRunner fallbackRunner)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
        _fallbackRunner = fallbackRunner ?? throw new ArgumentNullException(nameof(fallbackRunner));

        _preferencesService.PreferencesChanged += OnPreferencesChanged;

        _ = WarmUpAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _preferencesService.PreferencesChanged -= OnPreferencesChanged;
        await _processHost.DisposeAsync().ConfigureAwait(false);
        switch (_fallbackRunner)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _preferencesService.PreferencesChanged -= OnPreferencesChanged;
        _processHost.Dispose();
        (_fallbackRunner as IDisposable)?.Dispose();
    }

    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (_disposed)
            return await _fallbackRunner.RunAsync(startInfo, timeout, cancellationToken).ConfigureAwait(false);

        var executablePath = startInfo.FileName;
        var arguments = ExtractArguments(startInfo);

        try
        {
            return await _processHost.RunAsync(executablePath, arguments, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex,
                "Warm yt-dlp helper execution failed for '{ExecutablePath}'; falling back to direct process execution",
                executablePath);
            return await _fallbackRunner.RunAsync(startInfo, timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _ = RestartHelperAsync(preferences.YtDlpExecutablePath);
    }

    private async Task WarmUpAsync()
    {
        try
        {
            var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
            await _processHost.EnsureStartedAsync(executablePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Initial warm-up of yt-dlp helper host deferred or failed; will fallback on demand");
        }
    }

    private async Task RestartHelperAsync(string executablePath)
    {
        try
        {
            await _processHost.RestartAsync(executablePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Restart of yt-dlp helper host for '{ExecutablePath}' failed", executablePath);
        }
    }

    private static string[] ExtractArguments(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count > 0) return [.. startInfo.ArgumentList];

        return !string.IsNullOrWhiteSpace(startInfo.Arguments)
            ? startInfo.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];
    }
}