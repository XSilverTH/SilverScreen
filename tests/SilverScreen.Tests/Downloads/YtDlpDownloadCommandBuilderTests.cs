using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Downloads;

namespace SilverScreen.Tests.Downloads;

public sealed class YtDlpDownloadCommandBuilderTests
{
    private const string YtDlpPath = "yt-dlp";
    private const string ValidVideoId = "dQw4w9WgXcQ";

    private static DownloadJob CreateJob(DownloadOptions options, string videoId = ValidVideoId,
        string targetDir = "/tmp/downloads")
    {
        var video = new VideoSummary(videoId, "Test Video", "Test Channel", TimeSpan.FromMinutes(3), "", false);
        return new DownloadJob(
            Guid.NewGuid(),
            video,
            options,
            targetDir,
            null,
            DownloadJobStatus.Queued,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Build_LibraryDestination_OmitsSponsorBlockFlags()
    {
        var job = CreateJob(new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.Library,
            Quality = VideoQuality.P1080,
            PreferredCodec = PreferredVideoCodec.Vp9,
            Container = DownloadContainerFormat.Mkv,
            SponsorBlockMode = SponsorBlockDownloadMode.Strip
        });

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath, sponsorBlockCategories: ["sponsor", "intro"]);

        Assert.Contains("--progress", startInfo.ArgumentList);
        Assert.Contains("--newline", startInfo.ArgumentList);
        Assert.DoesNotContain("--sponsorblock-mark", startInfo.ArgumentList);
        Assert.DoesNotContain("--sponsorblock-remove", startInfo.ArgumentList);
        Assert.Contains("--merge-output-format", startInfo.ArgumentList);
        Assert.Contains("mkv", startInfo.ArgumentList);
        Assert.Contains("--embed-metadata", startInfo.ArgumentList);
        Assert.Contains("--embed-thumbnail", startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(SponsorBlockDownloadMode.MarkAsChapters, "--sponsorblock-mark")]
    [InlineData(SponsorBlockDownloadMode.Strip, "--sponsorblock-remove")]
    public void Build_CustomFolderDestination_AppliesSponsorBlockFlag(SponsorBlockDownloadMode mode,
        string expectedFlag)
    {
        var job = CreateJob(new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.CustomFolder,
            CustomFolderPath = "/tmp/external",
            SponsorBlockMode = mode
        }, targetDir: "/tmp/external");

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath, sponsorBlockCategories: ["sponsor", "outro"]);

        var flagIdx = startInfo.ArgumentList.IndexOf(expectedFlag);
        Assert.True(flagIdx >= 0 && flagIdx < startInfo.ArgumentList.Count - 1);
        Assert.Equal("sponsor,outro", startInfo.ArgumentList[flagIdx + 1]);
    }

    [Fact]
    public void Build_CustomFolderWithSponsorBlockOff_OmitsSponsorBlockFlags()
    {
        var job = CreateJob(new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.CustomFolder,
            CustomFolderPath = "/tmp/external",
            SponsorBlockMode = SponsorBlockDownloadMode.Off
        }, targetDir: "/tmp/external");

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath, sponsorBlockCategories: ["sponsor"]);

        Assert.DoesNotContain("--sponsorblock-mark", startInfo.ArgumentList);
        Assert.DoesNotContain("--sponsorblock-remove", startInfo.ArgumentList);
    }

    [Fact]
    public void Build_AudioOnlyMode_SetsExtractAudioAndContainerFormat()
    {
        var job = CreateJob(new DownloadOptions
        {
            MediaMode = DownloadMediaMode.AudioOnly,
            Container = DownloadContainerFormat.Opus,
            EmbedMetadata = true,
            EmbedThumbnail = true
        });

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath);

        Assert.Contains("-x", startInfo.ArgumentList);
        Assert.Contains("--audio-format", startInfo.ArgumentList);
        Assert.Contains("opus", startInfo.ArgumentList);
        Assert.Contains("--embed-thumbnail", startInfo.ArgumentList);
        Assert.DoesNotContain("--merge-output-format", startInfo.ArgumentList);
    }

    [Fact]
    public void Build_WebmVideoContainer_OmitsEmbedThumbnail()
    {
        var job = CreateJob(new DownloadOptions
        {
            MediaMode = DownloadMediaMode.VideoAndAudio,
            Container = DownloadContainerFormat.Webm,
            EmbedThumbnail = true
        });

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath);

        Assert.Contains("--merge-output-format", startInfo.ArgumentList);
        Assert.Contains("webm", startInfo.ArgumentList);
        Assert.DoesNotContain("--embed-thumbnail", startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(DownloadSubtitleMode.Embedded, "--embed-subs", null)]
    [InlineData(DownloadSubtitleMode.Sidecar, "--convert-subs", "srt")]
    public void Build_Subtitles_AddsExpectedSubtitleFlags(
        DownloadSubtitleMode mode,
        string expectedFlag,
        string? expectedArg)
    {
        var job = CreateJob(new DownloadOptions
        {
            SubtitleMode = mode,
            SubtitleLanguage = "ja,en"
        });

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath);

        Assert.Contains("--write-subs", startInfo.ArgumentList);
        Assert.Contains("--write-auto-subs", startInfo.ArgumentList);
        var langsIdx = startInfo.ArgumentList.IndexOf("--sub-langs");
        Assert.True(langsIdx >= 0);
        Assert.Equal("ja,en,-live_chat", startInfo.ArgumentList[langsIdx + 1]);
        Assert.Contains(expectedFlag, startInfo.ArgumentList);
        if (expectedArg is not null)
            Assert.Contains(expectedArg, startInfo.ArgumentList);
    }

    [Fact]
    public void Build_ForceOverwriteTrue_SetsForceOverwritesAndNoContinue()
    {
        var job = CreateJob(new DownloadOptions());

        var startInfo = YtDlpDownloadCommandBuilder.Build(job, YtDlpPath, forceOverwrite: true);

        Assert.Contains("--force-overwrites", startInfo.ArgumentList);
        Assert.Contains("--no-continue", startInfo.ArgumentList);
        Assert.DoesNotContain("--continue", startInfo.ArgumentList);
    }

    [Fact]
    public void Build_NonYouTubeVideoId_ThrowsArgumentException()
    {
        var job = CreateJob(new DownloadOptions(), "not-a-youtube-id");

        Assert.Throws<ArgumentException>(() => YtDlpDownloadCommandBuilder.Build(job, YtDlpPath));
    }

    [Fact]
    public void TryParseProgressLine_ParsesValidAndNaFields()
    {
        const string line = "SILVER_DL|downloading|1048576|NA|2097152|524288.5|12";

        var ok = YtDlpDownloadCommandBuilder.TryParseProgressLine(line, out var sample);

        Assert.True(ok);
        Assert.Equal("downloading", sample.Status);
        Assert.Equal(1048576L, sample.DownloadedBytes);
        Assert.Equal(2097152L, sample.TotalBytes);
        Assert.Equal(524288.5, sample.SpeedBytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(12), sample.Eta);
    }

    [Fact]
    public void TryParseFilePrintLine_ParsesPathHeightAndExt()
    {
        const string line = "SILVER_FILE|/tmp/downloads/Test [dQw4w9WgXcQ].mkv|1080|mkv";

        var ok = YtDlpDownloadCommandBuilder.TryParseFilePrintLine(
            line, YtDlpDownloadCommandBuilder.FinalFilePrefix, out var result);

        Assert.True(ok);
        Assert.Equal("/tmp/downloads/Test [dQw4w9WgXcQ].mkv", result.FilePath);
        Assert.Equal(1080, result.Height);
        Assert.Equal("mkv", result.Extension);
    }
}