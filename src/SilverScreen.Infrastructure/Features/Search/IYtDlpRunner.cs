using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Search;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
        CancellationToken cancellationToken);
}