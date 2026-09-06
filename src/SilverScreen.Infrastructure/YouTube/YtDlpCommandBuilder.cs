using System.Diagnostics;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
/// Builds <see cref="ProcessStartInfo"/> command specifications for direct yt-dlp invocations,
/// such as single-JSON manifest dumps for fallback format extraction and stream probing.
/// </summary>
public static class YtDlpCommandBuilder
{
    public static ProcessStartInfo BuildMediaExtraction(string executablePath, string videoId,
        string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(executablePath);
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-playlist");
        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookieFilePath);
        }

        startInfo.ArgumentList.Add(PlaybackRequest.BuildWatchUrl(videoId)
                                   ?? throw new ArgumentException("A valid YouTube video ID is required.",
                                       nameof(videoId)));
        return startInfo;
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
