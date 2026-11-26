using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Downloads;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Preferences;

namespace SilverScreen.Tests.Downloads;

public sealed class DownloadPreferencesAndUiStateTests
{
    [Fact]
    public void AppPreferences_DownloadPresetsAndSettings_RoundTripThroughAotContext()
    {
        var preset = new DownloadPreset(
            "p1",
            "1080p MKV",
            new DownloadOptions
            {
                DestinationKind = DownloadDestinationKind.Library,
                MediaMode = DownloadMediaMode.VideoAndAudio,
                Quality = VideoQuality.P1080,
                PreferredCodec = PreferredVideoCodec.Av1,
                Container = DownloadContainerFormat.Mkv,
                SubtitleMode = DownloadSubtitleMode.Embedded,
                SubtitleLanguage = "en,ja",
                EmbedMetadata = true,
                EmbedThumbnail = true,
                SponsorBlockMode = SponsorBlockDownloadMode.Strip
            });

        var prefs = new AppPreferences
        {
            LibraryDirectory = "/tmp/my-library",
            MaxConcurrentDownloads = 4,
            ShowCustomDownloadOption = false,
            DownloadPresets = new EquatableArray<DownloadPreset>([preset])
        };

        var json = JsonSerializer.Serialize(prefs, PreferencesJsonContext.Default.AppPreferences);
        var deserialized = JsonSerializer.Deserialize(json, PreferencesJsonContext.Default.AppPreferences);

        Assert.NotNull(deserialized);
        Assert.Equal("/tmp/my-library", deserialized.LibraryDirectory);
        Assert.Equal(4, deserialized.MaxConcurrentDownloads);
        Assert.False(deserialized.ShowCustomDownloadOption);
        var loadedPreset = Assert.Single(deserialized.DownloadPresets);
        Assert.Equal("1080p MKV", loadedPreset.Name);
        Assert.Equal(VideoQuality.P1080, loadedPreset.Options.Quality);
        Assert.Equal(DownloadContainerFormat.Mkv, loadedPreset.Options.Container);
        Assert.Equal(DownloadSubtitleMode.Embedded, loadedPreset.Options.SubtitleMode);
    }

    [Theory]
    [InlineData(0, true, DownloadTriggerBehavior.OpenDialog)]
    [InlineData(0, false, DownloadTriggerBehavior.OpenDialog)]
    [InlineData(1, false, DownloadTriggerBehavior.ImmediatePreset)]
    [InlineData(1, true, DownloadTriggerBehavior.ShowPresetMenu)]
    [InlineData(2, false, DownloadTriggerBehavior.ShowPresetMenu)]
    [InlineData(3, true, DownloadTriggerBehavior.ShowPresetMenu)]
    public void DownloadActionHelper_GetBehavior_AdaptsToPresetCountAndCustomToggle(
        int presetCount,
        bool showCustom,
        DownloadTriggerBehavior expected)
    {
        var presets = Enumerable.Range(0, presetCount)
            .Select(i => new DownloadPreset($"id-{i}", $"Preset {i}", new DownloadOptions()))
            .ToArray();

        var prefs = new AppPreferences
        {
            ShowCustomDownloadOption = showCustom,
            DownloadPresets = new EquatableArray<DownloadPreset>(presets)
        };

        Assert.Equal(expected, DownloadActionHelper.GetBehavior(prefs));
    }

    [Fact]
    public void QualityMismatchHelper_EvaluatesExpectedMismatchRules()
    {
        var video = new VideoSummary("dQw4w9WgXcQ", "Title", "Chan", TimeSpan.FromMinutes(2), "", false);

        var bestEntry = new LibraryEntry(video, "a.mp4", null, DateTimeOffset.UtcNow, 100,
            DownloadMediaMode.VideoAndAudio, VideoQuality.Best, 1080, "1080p", "MP4", DownloadSubtitleMode.None);
        Assert.False(QualityMismatchHelper.TryGetMismatchMessage(bestEntry, VideoQuality.Best, out _));
        Assert.True(QualityMismatchHelper.TryGetMismatchMessage(bestEntry, VideoQuality.P720, out var msg720));
        Assert.Contains("1080p", msg720);

        var p720Entry = bestEntry with { RequestedQuality = VideoQuality.P720, ActualHeight = 720, QualityLabel = "720p" };
        Assert.False(QualityMismatchHelper.TryGetMismatchMessage(p720Entry, VideoQuality.P720, out _));
        Assert.True(QualityMismatchHelper.TryGetMismatchMessage(p720Entry, VideoQuality.P1080, out _));
        Assert.True(QualityMismatchHelper.TryGetMismatchMessage(p720Entry, VideoQuality.Best, out _));

        var audioEntry = bestEntry with { MediaMode = DownloadMediaMode.AudioOnly, QualityLabel = "Audio" };
        Assert.True(QualityMismatchHelper.TryGetMismatchMessage(audioEntry, VideoQuality.Best, out var audioMsg));
        Assert.Contains("audio", audioMsg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MpvCommandBuilder_LocalFilePath_OmitsYtdlFlagsWhenAllLocalAndIncludesWhenMixed()
    {
        var localVideo = new VideoSummary("dQw4w9WgXcQ", "Local", "Chan", TimeSpan.FromMinutes(1), "", false,
            LocalFilePath: "/tmp/video.mkv");
        var remoteVideo = new VideoSummary("M7lc1UVf-VE", "Remote", "Chan", TimeSpan.FromMinutes(1), "", false);

        var localCmd = MpvCommandBuilder.Build(
            new PlaybackRequest([localVideo]),
            new PlaybackOptions { VideoQuality = "1080p" },
            cookieFilePath: "/tmp/cookies.txt");

        Assert.Equal("/tmp/video.mkv", localCmd.Arguments[^1]);
        Assert.DoesNotContain(localCmd.Arguments, a => a.StartsWith("--ytdl-", StringComparison.Ordinal));
        Assert.DoesNotContain("--cookies", localCmd.Arguments);

        var mixedCmd = MpvCommandBuilder.Build(
            new PlaybackRequest([localVideo, remoteVideo]),
            new PlaybackOptions { VideoQuality = "1080p" });

        Assert.Equal("/tmp/video.mkv", mixedCmd.Arguments[^2]);
        Assert.Equal("https://www.youtube.com/watch?v=M7lc1UVf-VE", mixedCmd.Arguments[^1]);
        Assert.Contains(mixedCmd.Arguments, a => a.StartsWith("--ytdl-format=", StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyInjection_RegistersLibraryAndDownloadServices()
    {
        var services = new ServiceCollection();
        services.AddSilverScreenServices(new ApplicationConfiguration());

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ILibraryService>());
        Assert.NotNull(provider.GetRequiredService<IDownloadService>());
        var deps = provider.GetRequiredService<global::SilverScreen.Player.PlayerDependencies>();
        Assert.NotNull(deps.Library);
        Assert.NotNull(deps.Downloads);
    }
}
