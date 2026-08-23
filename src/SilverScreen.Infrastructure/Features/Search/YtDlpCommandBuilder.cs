using System.Diagnostics;
using System.Globalization;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Search;

public static class YtDlpCommandBuilder
{
    public static ProcessStartInfo BuildSearch(SearchRequest request, YtDlpOptions options,
        string? cookieFilePath = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.StartIndex, 1);
        var startInfo = CreateStartInfo(options.ExecutablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        var pageSize = Math.Max(options.MaxResults, 1);
        startInfo.ArgumentList.Add("--playlist-start");
        startInfo.ArgumentList.Add(request.StartIndex.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--playlist-end");
        startInfo.ArgumentList.Add((request.StartIndex + pageSize - 1)
            .ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add($"ytsearch{request.StartIndex + pageSize - 1}:{request.Query}");
        return startInfo;
    }

    public static ProcessStartInfo BuildHome(string executablePath, int startIndex, string? cookieFilePath = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startIndex, 1);
        var startInfo = CreateStartInfo(executablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add("--playlist-start");
        startInfo.ArgumentList.Add(startIndex.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--playlist-end");
        startInfo.ArgumentList.Add((startIndex + 19).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(":ytrec");
        return startInfo;
    }

    public static ProcessStartInfo BuildHistory(string executablePath, int startIndex, string cookieFilePath)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startIndex, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieFilePath);
        var startInfo = CreateStartInfo(executablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add("--playlist-start");
        startInfo.ArgumentList.Add(startIndex.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--playlist-end");
        startInfo.ArgumentList.Add((startIndex + 19).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("https://www.youtube.com/feed/history");
        return startInfo;
    }

    public static ProcessStartInfo BuildChannel(string channelUrl, ChannelVideoSort sort, YtDlpOptions options,
        int startIndex, string? cookieFilePath = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startIndex, 1);
        if (!Uri.TryCreate(channelUrl, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Host, "www.youtube.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "youtube.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "m.youtube.com", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A valid YouTube channel URL is required.", nameof(channelUrl));

        var startInfo = CreateStartInfo(options.ExecutablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        var pageSize = Math.Max(options.MaxResults, 1);
        startInfo.ArgumentList.Add("--playlist-start");
        startInfo.ArgumentList.Add(startIndex.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--playlist-end");
        startInfo.ArgumentList.Add((startIndex + pageSize - 1).ToString(CultureInfo.InvariantCulture));

        var videosUrl = GetVideosUrl(channelUrl);
        startInfo.ArgumentList.Add(sort switch
        {
            ChannelVideoSort.Newest => videosUrl,
            ChannelVideoSort.Oldest => AppendQuery(videosUrl, "sort=da"),
            ChannelVideoSort.Popular => AppendQuery(videosUrl, "sort=p"),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null)
        });
        return startInfo;
    }


    public static ProcessStartInfo BuildVideoDetails(string executablePath, string videoId,
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

    public static ProcessStartInfo BuildComments(string executablePath, string videoId, YouTubeCommentSort sort,
        string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(executablePath);
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--write-comments");
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add(sort switch
        {
            YouTubeCommentSort.Top => "youtube:comment_sort=top;max_comments=200,100,100,25,2",
            YouTubeCommentSort.Newest => "youtube:comment_sort=new;max_comments=200,100,100,25,2",
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null)
        });

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

    private static string GetVideosUrl(string channelUrl)
    {
        var builder = new UriBuilder(channelUrl);
        var path = builder.Path.TrimEnd('/');
        if (!path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
            builder.Path = $"{path}/videos";
        return builder.Uri.AbsoluteUri;
    }

    private static string AppendQuery(string url, string query)
    {
        return $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{query}";
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    private static void AddCommonArguments(ProcessStartInfo startInfo, string? cookieFilePath)
    {
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtubetab:approximate_date");
        if (string.IsNullOrWhiteSpace(cookieFilePath)) return;
        startInfo.ArgumentList.Add("--cookies");
        startInfo.ArgumentList.Add(cookieFilePath);
    }
}