using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

public interface IYtDlpProcessHost : IAsyncDisposable, IDisposable
{
    bool IsRunning { get; }
    string? Version { get; }
    string? PythonVersion { get; }

    Task<ProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken);

    Task EnsureStartedAsync(string executablePath, CancellationToken cancellationToken = default);
    Task RestartAsync(string executablePath, CancellationToken cancellationToken = default);
}