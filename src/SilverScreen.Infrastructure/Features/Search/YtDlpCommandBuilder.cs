using System.Diagnostics;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.Features.Search;

public static class YtDlpCommandBuilder
{
    public static ProcessStartInfo BuildSearch(SearchRequest request, YtDlpOptions options,
        string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(options.ExecutablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add($"ytsearch{options.MaxResults}:{request.Query}");
        return startInfo;
    }

    public static ProcessStartInfo BuildHome(string executablePath, string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(executablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add(":ytrec");
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