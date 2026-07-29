using System.Diagnostics;

namespace SilverScreen.Infrastructure.Features.Search;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken);
}