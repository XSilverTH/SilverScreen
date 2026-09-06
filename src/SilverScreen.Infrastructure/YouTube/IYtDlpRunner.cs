using System.Diagnostics;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
/// Defines the contract for executing yt-dlp binary operations asynchronously with bounded timeouts and cancellation.
/// </summary>
/// <remarks>
/// Part of the direct extraction fallback infrastructure (<see cref="YtDlpMediaResolver"/>) used for format extraction
/// when not relying solely on mpv's internal <c>ytdl_hook.lua</c>.
/// </remarks>
public interface IYtDlpRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
        CancellationToken cancellationToken);
}