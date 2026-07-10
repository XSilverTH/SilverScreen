using SilverScreen.Core.Models;

namespace SilverScreen.Features.Search;

public interface IYtDlpRunner
{
    Task<ProcessResult> RunSearchAsync(SearchRequest request, YtDlpOptions options,
        CancellationToken cancellationToken);
}