using System.Diagnostics;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Player;

public sealed record MpvPlaybackCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

public sealed class MpvCommandBuilder
{
    /// <summary>
    ///     Builds the external-mpv argv for the FULL queue snapshot: watch URLs plus cookie
    ///     lease plus ytdl-format plus IPC endpoint. mpv+yt-dlp fetch formats and advance the
    ///     playlist itself. Resolved direct URLs are never used here. Pure: no I/O.
    /// </summary>
    public static MpvPlaybackCommand Build(PlaybackRequest request, PlaybackOptions options,
        string? cookieFilePath = null, string? inputIpcServerPath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ExternalMpvEnabled)
            throw new InvalidOperationException("External MPV playback is disabled.");

        if (string.IsNullOrWhiteSpace(options.MpvExecutablePath))
            throw new InvalidOperationException(RuntimeDependencyGuidance.MpvUnavailable(options.MpvExecutablePath));

        // Throws PlaybackRequest.EmptyQueueMessage when the snapshot is empty; the service
        // returns that text as the status string instead of throwing out of PlayAsync.
        var playbackUrls = GetPlaybackUrls(request);

        var arguments = new List<string>();
        if (options.Fullscreen)
            arguments.Add("--fs");

        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            arguments.Add("--cookies");
            arguments.Add($"--cookies-file={cookieFilePath}");
        }

        if (options.MarkWatchedVideos)
            arguments.Add("--ytdl-raw-options=mark-watched=");

        var ytdlFormat = BuildYtdlFormat(options.VideoQuality);
        if (ytdlFormat is not null)
            arguments.Add($"--ytdl-format={ytdlFormat}");

        if (!string.IsNullOrWhiteSpace(options.YtDlpExecutablePath))
            arguments.Add($"--script-opts=ytdl_hook-ytdl_path={options.YtDlpExecutablePath}");
        arguments.Add(options.AutoAdvanceNextVideo ? "--keep-open=yes" : "--keep-open=always");
        if (!string.IsNullOrWhiteSpace(inputIpcServerPath))
            arguments.Add($"--input-ipc-server={inputIpcServerPath}");

        if (request.EffectiveStartIndex > 0)
            arguments.Add($"--playlist-start={request.EffectiveStartIndex}");

        arguments.AddRange(playbackUrls);

        return new MpvPlaybackCommand(options.MpvExecutablePath, arguments);
    }

    public static IReadOnlyList<string> GetPlaybackUrls(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Videos.IsDefaultOrEmpty)
            throw new InvalidOperationException(PlaybackRequest.EmptyQueueMessage);

        var playbackUrls = new List<string>(request.Videos.Length);
        foreach (var playbackUrl in request.Videos.Select(video => string.IsNullOrWhiteSpace(video.WatchUrl)
                     ? PlaybackRequest.BuildWatchUrl(video.Id)
                     : video.WatchUrl))
        {
            if (string.IsNullOrWhiteSpace(playbackUrl))
                throw new InvalidOperationException("No playable URL is available.");

            if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Playback URL must be an absolute HTTP or HTTPS URL.");

            playbackUrls.Add(playbackUrl);
        }

        return playbackUrls;
    }

    public static string? BuildYtdlFormat(string videoQuality)
    {
        return YtDlpFormatSelector.ToMpvFormat(videoQuality);
    }

    public static ProcessStartInfo BuildStartInfo(MpvPlaybackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = false
        };

        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);

        return startInfo;
    }
}