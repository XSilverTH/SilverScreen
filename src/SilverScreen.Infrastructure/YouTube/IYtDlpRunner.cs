using SilverScreen.Infrastructure.Common;
using System.Diagnostics;

namespace SilverScreen.Infrastructure.YouTube;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken);
}