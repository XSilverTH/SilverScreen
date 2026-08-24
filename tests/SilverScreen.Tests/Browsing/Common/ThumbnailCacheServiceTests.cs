using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;

using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SilverScreen.Tests.Browsing.Common;

public sealed class ThumbnailCacheServiceTests
{
    [Fact]
    public async Task GetThumbnailAsync_DownloadsAndReusesCachedFile()
    {
        using var directory = new TemporaryDirectory();
        var bytes = "image"u8.ToArray();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var downloaded = await service.GetThumbnailAsync("https://example.com/image.jpg");
        var cached = await service.GetThumbnailAsync("https://example.com/image.jpg");

        Assert.NotNull(downloaded);
        Assert.False(downloaded.WasCacheHit);
        Assert.NotNull(cached);
        Assert.True(cached.WasCacheHit);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_OversizedDownloadDoesNotPopulateCache()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[11])
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, 10);

        var result = await service.GetThumbnailAsync("https://example.com/large.jpg");

        Assert.Null(result);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task GetThumbnailAsync_WebPDownloadIsCachedAndReturned()
    {
        using var directory = new TemporaryDirectory();
        var webp = "RIFF\0\0\0\0WEBP"u8.ToArray();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var downloaded = await service.GetThumbnailAsync("https://example.com/thumbnail.webp");
        var cached = await service.GetThumbnailAsync("https://example.com/thumbnail.webp");

        Assert.NotNull(downloaded);
        Assert.False(downloaded.WasCacheHit);
        Assert.True(File.Exists(downloaded.LocalPath));
        Assert.EndsWith(".webp", downloaded.LocalPath, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(cached);
        Assert.True(cached.WasCacheHit);
        Assert.Equal(downloaded.LocalPath, cached.LocalPath);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_YouTubeAvatarWithoutExtension_IsCachedAndReturned()
    {
        using var directory = new TemporaryDirectory();
        var webp = "RIFF\0\0\0\0WEBP"u8.ToArray();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        const string avatarUrl = "https://yt3.ggpht.com/ytc/AIdro_abc123=s176-c-k-c0x00ffffff-no-rj";
        var downloaded = await service.GetThumbnailAsync(avatarUrl);
        var cached = await service.GetThumbnailAsync(avatarUrl);

        Assert.NotNull(downloaded);
        Assert.False(downloaded.WasCacheHit);
        Assert.True(File.Exists(downloaded.LocalPath));

        Assert.NotNull(cached);
        Assert.True(cached.WasCacheHit);
        Assert.Equal(downloaded.LocalPath, cached.LocalPath);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_SendsAcceptHeaderWithWebP()
    {
        using var directory = new TemporaryDirectory();
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. "image"u8])
            });
        });
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        await service.GetThumbnailAsync("https://example.com/image.jpg");

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.TryGetValues("Accept", out var values));
        var acceptHeader = string.Join(", ", values);
        Assert.Contains("image/webp", acceptHeader);
    }

    [Fact]
    public async Task GetThumbnailAsync_VideoSummary_WebPThumbnail_IsCachedAndReturned()
    {
        using var directory = new TemporaryDirectory();
        var webp = "RIFF\0\0\0\0WEBP"u8.ToArray();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var video = new VideoSummary("testVid123", "Test Title", "Test Channel", TimeSpan.FromMinutes(5),
            "https://i.ytimg.com/vi_webp/testVid123/maxresdefault.webp", false);

        var result = await service.GetThumbnailAsync(video);

        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);
        Assert.True(File.Exists(result.LocalPath));
    }

    [Fact]
    public async Task GetThumbnailAsync_ExistingCachedWebpFile_IsNotDeleted()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        // Pre-populate cache with a webp file
        const string url = "https://example.com/image.webp";
        var cachePath = Path.Combine(directory.Path,
            $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))}.webp");
        var webp = "RIFF\0\0\0\0WEBP"u8.ToArray();
        await File.WriteAllBytesAsync(cachePath, webp);

        var result = await service.GetThumbnailAsync(url);

        Assert.NotNull(result);
        Assert.True(result.WasCacheHit);
        Assert.True(File.Exists(cachePath));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_EvictsOldestFiles_WhenLimitExceeded()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. "image"u8])
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: 3);

        var r1 = await service.GetThumbnailAsync("https://example.com/1.jpg");
        var r2 = await service.GetThumbnailAsync("https://example.com/2.jpg");
        var r3 = await service.GetThumbnailAsync("https://example.com/3.jpg");
        var r4 = await service.GetThumbnailAsync("https://example.com/4.jpg");
        var r5 = await service.GetThumbnailAsync("https://example.com/5.jpg");

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.NotNull(r3);
        Assert.NotNull(r4);
        Assert.NotNull(r5);

        Assert.Equal(3, service.CachedFileCount);
        var diskFiles = Directory.EnumerateFiles(directory.Path).Where(f => !f.EndsWith(".tmp")).ToList();
        Assert.Equal(3, diskFiles.Count);

        Assert.False(File.Exists(r1.LocalPath));
        Assert.False(File.Exists(r2.LocalPath));
        Assert.True(File.Exists(r3.LocalPath));
        Assert.True(File.Exists(r4.LocalPath));
        Assert.True(File.Exists(r5.LocalPath));
    }

    [Fact]
    public async Task GetThumbnailAsync_CacheHitUpdatesLru_PreventsEviction()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. "image"u8])
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: 3);

        var r1 = await service.GetThumbnailAsync("https://example.com/1.jpg");
        var r2 = await service.GetThumbnailAsync("https://example.com/2.jpg");
        var r3 = await service.GetThumbnailAsync("https://example.com/3.jpg");

        // Hit 1 again, promoting it to MRU (order is now: 2 [oldest], 3, 1 [newest])
        var r1Hit = await service.GetThumbnailAsync("https://example.com/1.jpg");
        Assert.NotNull(r1Hit);
        Assert.True(r1Hit.WasCacheHit);

        // Add 4 -> should evict 2 (oldest), keeping 3, 1, 4
        var r4 = await service.GetThumbnailAsync("https://example.com/4.jpg");
        Assert.NotNull(r4);

        Assert.Equal(3, service.CachedFileCount);
        Assert.True(File.Exists(r1!.LocalPath));
        Assert.False(File.Exists(r2!.LocalPath));
        Assert.True(File.Exists(r3!.LocalPath));
        Assert.True(File.Exists(r4.LocalPath));
    }

    [Fact]
    public async Task GetThumbnailAsync_ConcurrentDownloads_RespectsMaxFileCount()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler(async (_, _) =>
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. "image"u8])
            };
        });
        using var client = new HttpClient(handler);
        const int maxFiles = 5;
        const int totalDownloads = 40;
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: maxFiles);

        var tasks = Enumerable.Range(0, totalDownloads)
            .Select(i => service.GetThumbnailAsync($"https://example.com/concurrent_{i}.jpg"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.NotNull);
        Assert.Equal(maxFiles, service.CachedFileCount);
        var diskFiles = Directory.EnumerateFiles(directory.Path).Where(f => !f.EndsWith(".tmp")).ToList();
        Assert.Equal(maxFiles, diskFiles.Count);
    }

    [Fact]
    public async Task GetThumbnailAsync_ConcurrentRequestsForSameUrl_ReturnsSameCachedFile()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler(async (_, _) =>
        {
            await Task.Delay(10, CancellationToken.None);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([.. "image"u8])
            };
        });
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: 10);

        const string url = "https://example.com/same_image.jpg";
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.GetThumbnailAsync(url))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.NotNull);
        var firstPath = results[0]!.LocalPath;
        Assert.All(results, r => Assert.Equal(firstPath, r!.LocalPath));
        Assert.True(File.Exists(firstPath));
        Assert.Equal(1, service.CachedFileCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_PreExistingFilesOnDisk_AreDiscoveredAndEvicted()
    {
        using var directory = new TemporaryDirectory();
        // Pre-create 5 files on disk with staggered timestamps
        var filePaths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(directory.Path, $"pre_existing_{i}.img");
            await File.WriteAllBytesAsync(path, [.. "old_image"u8]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i - 10));
            filePaths.Add(path);
        }

        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. "new_image"u8])
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: 3);

        // Adding 1 new file when 5 pre-existed should trigger init (which evicts oldest 2, keeping 3),
        // and then adding the new file evicts 1 more so total is 3.
        var newResult = await service.GetThumbnailAsync("https://example.com/new_download.jpg");

        Assert.NotNull(newResult);
        Assert.Equal(3, service.CachedFileCount);
        var remainingFiles = Directory.EnumerateFiles(directory.Path).Where(f => !f.EndsWith(".tmp")).ToList();
        Assert.Equal(3, remainingFiles.Count);

        // Oldest pre-existing files (0, 1, 2) should be gone
        Assert.False(File.Exists(filePaths[0]));
        Assert.False(File.Exists(filePaths[1]));
        Assert.False(File.Exists(filePaths[2]));
        // Pre-existing (3, 4) and new download should exist
        Assert.True(File.Exists(filePaths[3]));
        Assert.True(File.Exists(filePaths[4]));
        Assert.True(File.Exists(newResult.LocalPath));
    }

    [Fact]
    public async Task GetThumbnailAsync_TemporaryFilesFromAbortedDownloads_CleanedUpOnInit()
    {
        using var directory = new TemporaryDirectory();
        var staleTmpFile = Path.Combine(directory.Path, "orphaned.12345.tmp");
        await File.WriteAllBytesAsync(staleTmpFile, [.. "junk"u8]);
        File.SetLastWriteTimeUtc(staleTmpFile, DateTime.UtcNow.AddMinutes(-10));
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([.. "image"u8])
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path, maxFileCount: 5);

        var result = await service.GetThumbnailAsync("https://example.com/image.jpg");

        Assert.NotNull(result);
        Assert.False(File.Exists(staleTmpFile));
        Assert.Equal(1, service.CachedFileCount);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"silverscreen-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request, cancellationToken);
        }
    }
}
