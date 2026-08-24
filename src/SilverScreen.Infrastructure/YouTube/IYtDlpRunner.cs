using System.Diagnostics;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken);
}