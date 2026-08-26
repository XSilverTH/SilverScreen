using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Serilog;
using SilverScreen.Infrastructure.Common;
using Process = System.Diagnostics.Process;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpProcessHost : IYtDlpProcessHost
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpProcessHost>();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ProcessResult>> _pendingRequests = new();

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _currentExecutablePath;
    private bool _disposed;
    private Task? _errorReadTask;
    private Task? _outputReadTask;

    private Process? _process;
    private long _requestIdCounter;

    public bool IsRunning => _process is { HasExited: false };
    public string? Version { get; private set; }
    public string? PythonVersion { get; private set; }

    public async Task EnsureStartedAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning && string.Equals(_currentExecutablePath, executablePath, StringComparison.Ordinal)) return;

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && string.Equals(_currentExecutablePath, executablePath, StringComparison.Ordinal)) return;

            await StopInternalAsync().ConfigureAwait(false);
            await StartInternalAsync(executablePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task RestartAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
            await StartInternalAsync(executablePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureStartedAsync(executablePath, cancellationToken).ConfigureAwait(false);

        var process = _process;
        if (process is null || process.HasExited)
        {
            await EnsureStartedAsync(executablePath, cancellationToken).ConfigureAwait(false);
            process = _process ?? throw new InvalidOperationException("yt-dlp helper process is not available.");
        }

        var requestId = Interlocked.Increment(ref _requestIdCounter).ToString(CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        var request = new YtDlpIpcRequest
        {
            Id = requestId,
            Action = "run",
            Args = [.. arguments]
        };

        var requestJson = JsonSerializer.Serialize(request, YtDlpIpcJsonContext.Default.YtDlpIpcRequest);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        await using var reg = timeoutCts.Token.Register(() =>
        {
            if (!_pendingRequests.TryRemove(requestId, out var removedTcs)) return;
            if (cancellationToken.IsCancellationRequested)
                removedTcs.TrySetCanceled(cancellationToken);
            else
                removedTcs.TrySetException(new TimeoutException(
                    $"yt-dlp helper request timed out after {timeout.TotalSeconds:0} seconds."));
        });

        await _writeLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopInternalAsync().GetAwaiter().GetResult();
        _startLock.Dispose();
        _writeLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopInternalAsync().ConfigureAwait(false);
        _startLock.Dispose();
        _writeLock.Dispose();
    }

    private async Task StartInternalAsync(string executablePath, CancellationToken cancellationToken)
    {
        var resolvedExecutable = ResolveExecutableFullPath(executablePath) ?? executablePath;
        var candidates = GetPythonCandidates(executablePath);

        Exception? lastException = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = candidate,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(YtDlpHostScript.Script);

            startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            if (!string.IsNullOrWhiteSpace(resolvedExecutable))
                startInfo.Environment["SILVERSCREEN_YT_DLP_PATH"] = resolvedExecutable;

            Process? process = null;
            try
            {
                Logger.Debug("Attempting to start yt-dlp helper host using Python candidate '{Candidate}'", candidate);
                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    process.Dispose();
                    continue;
                }

                var handshakeLine = await ReadLineWithTimeoutAsync(
                    process.StandardOutput,
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(handshakeLine))
                {
                    TryKill(process);
                    process.Dispose();
                    continue;
                }

                var handshake = JsonSerializer.Deserialize(handshakeLine, YtDlpIpcJsonContext.Default.YtDlpIpcResponse);
                if (handshake is not null && string.Equals(handshake.Type, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    Version = handshake.Version;
                    PythonVersion = handshake.Python;
                    _currentExecutablePath = executablePath;
                    _process = process;

                    _outputReadTask = Task.Run(ReadOutputLoopAsync, CancellationToken.None);
                    _errorReadTask = Task.Run(ReadErrorLoopAsync, CancellationToken.None);

                    Logger.Information(
                        "Warm yt-dlp helper host started successfully (yt-dlp version {Version}, Python {PythonVersion}) using {Candidate}",
                        Version, PythonVersion, candidate);
                    return;
                }

                if (handshake is not null && string.Equals(handshake.Type, "error", StringComparison.OrdinalIgnoreCase))
                    Logger.Warning("yt-dlp helper startup returned error: {Message}", handshake.Message);

                TryKill(process);
                process.Dispose();
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (process is not null)
                {
                    TryKill(process);
                    process.Dispose();
                }

                Logger.Debug(ex, "Failed to start yt-dlp helper candidate '{Candidate}'", candidate);
            }
        }

        throw new InvalidOperationException(
            $"Unable to start yt-dlp helper host for '{executablePath}'.", lastException);
    }

    private async Task ReadOutputLoopAsync()
    {
        var process = _process;
        if (process is null) return;

        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;

                line = line.Trim();
                if (line.Length == 0) continue;

                try
                {
                    var response = JsonSerializer.Deserialize(line, YtDlpIpcJsonContext.Default.YtDlpIpcResponse);
                    if (response?.Id is not null && _pendingRequests.TryRemove(response.Id, out var tcs))
                    {
                        var result = new ProcessResult(
                            response.ExitCode ?? 0,
                            response.Stdout ?? string.Empty,
                            response.Stderr ?? string.Empty);
                        tcs.TrySetResult(result);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.Warning(ex, "Failed to parse yt-dlp helper IPC response line: {Line}", line);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "yt-dlp helper stdout read loop completed or terminated");
        }
        finally
        {
            var ex = new InvalidOperationException("yt-dlp helper process exited unexpectedly.");
            foreach (var kvp in _pendingRequests)
                if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                    tcs.TrySetException(ex);
        }
    }

    private async Task ReadErrorLoopAsync()
    {
        var process = _process;
        if (process is null) return;

        try
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;

                if (!string.IsNullOrWhiteSpace(line)) Logger.Debug("[yt-dlp-helper-stderr] {Line}", line);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "yt-dlp helper stderr read loop terminated");
        }
    }

    private async Task StopInternalAsync()
    {
        var process = _process;
        _process = null;

        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                var shutdownRequest = new YtDlpIpcRequest { Action = "shutdown" };
                var shutdownJson =
                    JsonSerializer.Serialize(shutdownRequest, YtDlpIpcJsonContext.Default.YtDlpIpcRequest);

                if (await _writeLock.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false))
                    try
                    {
                        await process.StandardInput.WriteLineAsync(shutdownJson).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignored
                    }
                    finally
                    {
                        _writeLock.Release();
                    }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            TryKill(process);
        }
        finally
        {
            TryKill(process);
            process.Dispose();
        }

        if (_outputReadTask is not null)
        {
            try
            {
                await _outputReadTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            _outputReadTask = null;
        }

        if (_errorReadTask is not null)
        {
            try
            {
                await _errorReadTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            _errorReadTask = null;
        }
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        StreamReader reader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static List<string> GetPythonCandidates(string executablePath)
    {
        var candidates = new List<string>(4);
        var resolvedPath = ResolveExecutableFullPath(executablePath);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            var shebang = TryReadShebang(resolvedPath);
            if (!string.IsNullOrWhiteSpace(shebang) &&
                shebang.Contains("python", StringComparison.OrdinalIgnoreCase))
                candidates.Add(shebang);
        }

        candidates.Add("python3");
        candidates.Add("python");

        return [.. candidates.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string? TryReadShebang(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.StartsWith("#!")) return null;

            var command = firstLine[2..].Trim();
            if (command.StartsWith("/usr/bin/env ", StringComparison.Ordinal))
            {
                var parts = command["/usr/bin/env ".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0] : null;
            }

            var cmdParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return cmdParts.Length > 0 ? cmdParts[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExecutableFullPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        var trimmed = executablePath.Trim();
        if (Path.IsPathFullyQualified(trimmed) ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath)) return null;

        foreach (var dir in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, trimmed);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            if (!OperatingSystem.IsWindows() || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var exeCandidate = Path.Combine(dir, $"{trimmed}.exe");
            if (File.Exists(exeCandidate)) return Path.GetFullPath(exeCandidate);
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // ignored
        }
    }
}