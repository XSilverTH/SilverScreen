using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Account.Auth;

public interface INativeAuthService : IDisposable
{
    bool IsAuthenticating { get; }
    Task<bool> StartAuthAsync(CancellationToken cancellationToken = default);
}

public sealed class NativeAuthService : INativeAuthService
{
    private static readonly ILogger Logger = Log.ForContext<NativeAuthService>();
    private readonly ISessionService _sessionService;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly Func<string?> _processPathResolver;
    private readonly CancellationTokenSource _disposalCts = new();
    private Process? _currentProcess;
    private int _isRunning;
    private bool _disposed;

    public NativeAuthService(ISessionService sessionService)
        : this(sessionService, static psi => Process.Start(psi), static () => Environment.ProcessPath)
    {
    }

    internal NativeAuthService(
        ISessionService sessionService,
        Func<ProcessStartInfo, Process?> processStarter,
        Func<string?> processPathResolver)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _processPathResolver = processPathResolver ?? throw new ArgumentNullException(nameof(processPathResolver));
    }

    public bool IsAuthenticating => Volatile.Read(ref _isRunning) != 0;

    public async Task<bool> StartAuthAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            Logger.Information("Native auth worker is already in progress");
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposalCts.Token);

        try
        {
            var binaryPath = _processPathResolver();
            if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath))
            {
                binaryPath = "/proc/self/exe";
            }

            Logger.Information("Starting native auth worker: {BinaryPath} --auth", binaryPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--auth",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = _processStarter(startInfo);
            if (process is null)
            {
                Logger.Error("Failed to launch native auth worker process");
                return false;
            }

            using (process)
            {
                _currentProcess = process;
                try
                {
                    var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                    var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

                    await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);

                    var stdout = await stdoutTask.ConfigureAwait(false);
                    var stderr = await stderrTask.ConfigureAwait(false);

                    if (process.ExitCode != 0)
                    {
                        Logger.Information(
                            "Native auth worker exited with code {ExitCode} (user cancelled or closed)",
                            process.ExitCode);
                        if (!string.IsNullOrWhiteSpace(stderr))
                        {
                            Logger.Debug("Native auth worker stderr: {Stderr}", stderr);
                        }
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(stdout))
                    {
                        Logger.Warning("Native auth worker exited with code 0 but wrote no cookies to stdout");
                        return false;
                    }

                    Logger.Information("Native auth worker completed successfully; saving session");
                    await _sessionService.SetManualSessionAsync(stdout.Trim(), SessionCookieFormat.NetscapeCookiesText)
                        .ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    _currentProcess = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Information("Native auth worker execution was cancelled");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _disposalCts.Cancel();
        }
        catch (ObjectDisposedException) { }

        try
        {
            var proc = _currentProcess;
            if (proc is { HasExited: false })
            {
                proc.Kill();
            }
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Error killing running native auth worker during disposal");
        }

        _disposalCts.Dispose();
    }
}
