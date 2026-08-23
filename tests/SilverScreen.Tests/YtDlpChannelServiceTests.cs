using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Tests;

public sealed class YtDlpChannelServiceTests
{
    [Fact]
    public async Task GetChannelAsync_MapsMetadataVideosAndRequestedSort()
    {
        var runner = new CapturingRunner(new ProcessResult(0, """
                                                              {
                                                                "channel": "Example Channel",
                                                                "description": "Videos about examples.",
                                                                "thumbnail": "https://img.example/avatar.jpg",
                                                                "channel_follower_count": 1234,
                                                                "entries": [
                                                                  {
                                                                    "id": "dQw4w9WgXcQ",
                                                                    "title": "Example video",
                                                                    "channel": "Example Channel",
                                                                    "channel_url": "https://www.youtube.com/@example",
                                                                    "duration": 42
                                                                  }
                                                                ]
                                                              }
                                                              """, ""));
        var service = CreateService(runner);

        var page = await service.GetChannelAsync("https://www.youtube.com/@example", "Fallback",
            ChannelVideoSort.Popular, 1, CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal("Example Channel", page.Name);
        Assert.Equal("Videos about examples.", page.Description);
        Assert.Equal("https://img.example/avatar.jpg", page.AvatarUrl);
        Assert.Equal(1234, page.SubscriberCount);
        var video = Assert.Single(page.Videos);
        Assert.Equal("https://www.youtube.com/@example", video.ChannelUrl);
    }


    private static YtDlpChannelService CreateService(CapturingRunner runner)
    {
        return new YtDlpChannelService(new TestPreferences(), runner, new FakeCookieFileProvider());
    }

    private sealed class CapturingRunner(ProcessResult result) : IYtDlpRunner
    {
        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class TestPreferences : IPreferencesService
    {
        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return new AppPreferences { YtDlpExecutablePath = "yt-dlp-test", MaxResults = 25 };
        }

        public void SavePreferences(AppPreferences preferences)
        {
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class FakeCookieFileProvider : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile()
        {
            return null;
        }
    }
}