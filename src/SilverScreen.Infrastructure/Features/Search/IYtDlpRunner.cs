using System.Diagnostics;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Search;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
        CancellationToken cancellationToken);
}

public interface IYtDlpProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken);
}