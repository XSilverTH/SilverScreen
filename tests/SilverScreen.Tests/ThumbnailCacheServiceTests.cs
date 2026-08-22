using System.Net;
using SilverScreen.Infrastructure.Features.Thumbnails;

namespace SilverScreen.Tests;

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
        var webp = new byte[]
            { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
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
        var webp = new byte[]
            { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var avatarUrl = "https://yt3.ggpht.com/ytc/AIdro_abc123=s176-c-k-c0x00ffffff-no-rj";
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
                Content = new ByteArrayContent("image"u8.ToArray())
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
        var webp = new byte[]
            { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var video = new Core.Models.VideoSummary("testVid123", "Test Title", "Test Channel", TimeSpan.FromMinutes(5),
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
        var url = "https://example.com/image.webp";
        var cachePath = Path.Combine(directory.Path,
            $"{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))}.webp");
        var webp = new byte[]
            { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        await File.WriteAllBytesAsync(cachePath, webp);

        var result = await service.GetThumbnailAsync(url);

        Assert.NotNull(result);
        Assert.True(result.WasCacheHit);
        Assert.True(File.Exists(cachePath));
        Assert.Equal(0, handler.CallCount);
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