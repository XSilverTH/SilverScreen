using System.ComponentModel;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpChannelService(
    IPreferencesService preferencesService,
    IYtDlpRunner runner,
    ICookieFileProvider cookieFileProvider) : IChannelService
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpChannelService>();

    public async Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
        int startIndex, CancellationToken cancellationToken)
    {
        Logger.Information("Loading channel page for {ChannelUrl} (Sort: {Sort}, StartIndex: {StartIndex})", channelUrl, sort, startIndex);
        var preferences = preferencesService.GetPreferences();
        var options = new YtDlpOptions
        {
            ExecutablePath = preferences.YtDlpExecutablePath,
            MaxResults = preferences.MaxResults
        };

        try
        {
            using var cookieFile = cookieFileProvider.CreateCookieFile();
            var result = await runner.RunAsync(
                YtDlpCommandBuilder.BuildChannel(channelUrl, sort, options, startIndex, cookieFile?.Path), options.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"yt-dlp exited with code {result.ExitCode}."
                    : result.StandardError.Trim();
                Logger.Warning("yt-dlp channel request exited with code {ExitCode}", result.ExitCode);
                return ChannelPage.Failed(channelUrl, fallbackName, sort,
                    $"Could not load channel: {RuntimeDependencyGuidance.YtDlpFailed(error)}");
            }

            return ParsePage(result.StandardOutput, channelUrl, fallbackName, sort,
                startIndex, Math.Max(options.MaxResults, 1));
        }
        catch (Win32Exception exception)
        {
            Logger.Warning(exception, "yt-dlp is not installed or could not be started for channel request");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: {RuntimeDependencyGuidance.YtDlpUnavailable(options.ExecutablePath)}");
        }
        catch (JsonException exception)
        {
            Logger.Warning(exception, "yt-dlp returned invalid JSON for channel request");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: yt-dlp returned invalid JSON ({exception.Message}).");
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "yt-dlp channel request timed out");
            return ChannelPage.Failed(channelUrl, fallbackName, sort,
                $"Could not load channel: {RuntimeDependencyGuidance.YtDlpTimedOut}");
        }
    }

    private static ChannelPage ParsePage(string output, string channelUrl, string fallbackName, ChannelVideoSort sort,
        int startIndex, int pageSize)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var pageEntries = YtDlpVideoParser.Parse(output).ToArray();
        var videos = pageEntries
            .Where(video => !video.IsShort)
            .DistinctBy(video => video.Id)
            .ToArray();
        var name = FirstString(root, "channel", "uploader", "title") ?? fallbackName;
        var description = FirstString(root, "description");
        var avatarUrl = FirstString(root, "thumbnail") ?? GetThumbnailUrl(root);
        var subscriberCount = GetInt64(root, "channel_follower_count", "uploader_follower_count");
        var status = videos.Length == 0 ? "This channel has no videos to show." : null;

        int? nextStartIndex = pageEntries.Length == pageSize ? startIndex + pageSize : null;
        return new ChannelPage(channelUrl, name, description, avatarUrl, subscriberCount, videos, sort, status,
            NextStartIndex: nextStartIndex);
    }

    private static string? GetThumbnailUrl(JsonElement root)
    {
        if (!root.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return null;

        return thumbnails.EnumerateArray()
            .Select(thumbnail => FirstString(thumbnail, "url"))
            .LastOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private static long? GetInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out var value))
                return value;

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

        return null;
    }
}
