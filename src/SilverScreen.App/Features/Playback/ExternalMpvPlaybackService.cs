using System.ComponentModel;
using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService
{
    private readonly PlaybackOptions _options;
    private readonly MpvCommandBuilder _commandBuilder;

    public ExternalMpvPlaybackService()
        : this(new PlaybackOptions(), new MpvCommandBuilder())
    {
    }

    public ExternalMpvPlaybackService(PlaybackOptions options, MpvCommandBuilder commandBuilder)
    {
        _options = options;
        _commandBuilder = commandBuilder;
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        try
        {
            var command = _commandBuilder.Build(request, _options);
            var startInfo = _commandBuilder.BuildStartInfo(command);

            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                return "Could not start MPV. Is it installed?";
            }

            return $"Opening in MPV: {request.Title}";
        }
        catch (Win32Exception)
        {
            return "Could not start MPV. Is it installed?";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
}
