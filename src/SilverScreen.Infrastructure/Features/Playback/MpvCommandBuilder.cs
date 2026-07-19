using System.Diagnostics;
using SilverScreen.Core.Models;

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
            throw new InvalidOperationException("MPV executable path is not configured.");

        if (request.Videos.IsDefaultOrEmpty)
            throw new InvalidOperationException("No videos were provided for playback.");

        var playbackUrls = new List<string>(request.Videos.Length);
        foreach (var video in request.Videos)
        {
            var playbackUrl = string.IsNullOrWhiteSpace(video.WatchUrl)
                ? PlaybackRequest.BuildWatchUrl(video.Id)
                : video.WatchUrl;
            if (string.IsNullOrWhiteSpace(playbackUrl))
                throw new InvalidOperationException("No playable URL is available.");

            if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Playback URL must be an absolute HTTP or HTTPS URL.");

            playbackUrls.Add(playbackUrl);
        }
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            var ytdlOptions = $"cookies={cookieFilePath}";
            if (options.MarkWatchedVideos)
                ytdlOptions += ",mark-watched=";

            arguments.Add($"--ytdl-raw-options={ytdlOptions}");
        }

        if (!string.IsNullOrWhiteSpace(options.VideoQuality) &&
            !options.VideoQuality.Equals("Best", StringComparison.OrdinalIgnoreCase))
        {
            var heightStr = options.VideoQuality.Replace("p", "", StringComparison.OrdinalIgnoreCase);
            if (int.TryParse(heightStr, out var height))
                arguments.Add($"--ytdl-format=bestvideo[height<={height}]+bestaudio/best[height<={height}]");
        }

        arguments.AddRange(playbackUrls);

        return new MpvPlaybackCommand(options.MpvExecutablePath, arguments);
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