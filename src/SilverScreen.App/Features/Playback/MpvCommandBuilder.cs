using System.Diagnostics;
using SilverScreen.Core.Models;

namespace SilverScreen.Features.Playback;

public sealed record MpvPlaybackCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

public sealed class MpvCommandBuilder
{
    public MpvPlaybackCommand Build(PlaybackRequest request, PlaybackOptions options, string? cookieFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ExternalMpvEnabled)
        {
            throw new InvalidOperationException("External MPV playback is disabled.");
        }

        if (string.IsNullOrWhiteSpace(options.MpvExecutablePath))
        {
            throw new InvalidOperationException("MPV executable path is not configured.");
        }

        var playbackUrl = request.PlaybackUrl;
        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            throw new InvalidOperationException("No playable URL is available for this mock video yet.");
        }

        if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Playback URL must be an absolute HTTP or HTTPS URL.");
        }

        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            arguments.Add($"--ytdl-raw-options=cookies={cookieFilePath}");
        }

        arguments.Add(playbackUrl);

        return new MpvPlaybackCommand(options.MpvExecutablePath, arguments);
    }

    public ProcessStartInfo BuildStartInfo(MpvPlaybackCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = false,
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
