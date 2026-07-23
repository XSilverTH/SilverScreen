using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed record MpvPlaybackCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

public sealed class MpvCommandBuilder
{
    public static MpvPlaybackCommand Build(PlaybackRequest request, PlaybackOptions options,
        string? cookieFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ExternalMpvEnabled)
            throw new InvalidOperationException("External MPV playback is disabled.");

        if (string.IsNullOrWhiteSpace(options.MpvExecutablePath))
            throw new InvalidOperationException(RuntimeDependencyGuidance.MpvUnavailable(options.MpvExecutablePath));

        var playbackUrls = GetPlaybackUrls(request);

        var arguments = new List<string>();
        if (options.Fullscreen)
            arguments.Add("--fs");
        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            var ytdlOptions = $"cookies={cookieFilePath}";
            if (options.MarkWatchedVideos)
                ytdlOptions += ",mark-watched=";

            arguments.Add($"--ytdl-raw-options={ytdlOptions}");
        }

        var ytdlFormat = BuildYtdlFormat(options.VideoQuality);
        if (ytdlFormat is not null)
            arguments.Add($"--ytdl-format={ytdlFormat}");

        arguments.AddRange(playbackUrls);

        return new MpvPlaybackCommand(options.MpvExecutablePath, arguments);
    }

    public static IReadOnlyList<string> GetPlaybackUrls(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Videos.IsDefaultOrEmpty)
            throw new InvalidOperationException("No videos were provided for playback.");

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
        return videoQuality switch
        {
            "Best" => null,
            "1080p" => "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            "720p" => "bestvideo[height<=720]+bestaudio/best[height<=720]",
            "480p" => "bestvideo[height<=480]+bestaudio/best[height<=480]",
            "360p" => "bestvideo[height<=360]+bestaudio/best[height<=360]",
            _ => null
        };
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