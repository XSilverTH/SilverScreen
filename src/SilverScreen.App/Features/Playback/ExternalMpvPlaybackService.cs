using System.ComponentModel;
using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Session;

namespace SilverScreen.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService
{
    private readonly PlaybackOptions _options;
    private readonly MpvCommandBuilder _commandBuilder;
    private readonly ICookieFileProvider? _cookieFileProvider;

    public ExternalMpvPlaybackService()
        : this(new PlaybackOptions(), new MpvCommandBuilder())
    {
    }

    public ExternalMpvPlaybackService(
        PlaybackOptions options,
        MpvCommandBuilder commandBuilder,
        ICookieFileProvider? cookieFileProvider = null)
    {
        _options = options;
        _commandBuilder = commandBuilder;
        _cookieFileProvider = cookieFileProvider;
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        CookieFileLease? cookieFile = null;

        try
        {
            cookieFile = _cookieFileProvider?.CreateCookieFile();
            var command = _commandBuilder.Build(request, _options, cookieFile?.Path);
            var startInfo = _commandBuilder.BuildStartInfo(command);

            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                cookieFile?.Dispose();
                return "Could not start MPV. Is it installed?";
            }

            if (cookieFile is not null)
            {
                started.EnableRaisingEvents = true;
                started.Exited += (_, _) => cookieFile.Dispose();
                cookieFile = null;
            }

            return $"Opening in MPV: {request.Title}";
        }
        catch (Win32Exception)
        {
            cookieFile?.Dispose();
            return "Could not start MPV. Is it installed?";
        }
        catch (InvalidOperationException ex)
        {
            cookieFile?.Dispose();
            return ex.Message;
        }
    }
}
