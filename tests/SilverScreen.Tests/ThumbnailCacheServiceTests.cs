using System.Net;
using SilverScreen.Infrastructure.Features.Thumbnails;

namespace SilverScreen.Tests;

public sealed class ThumbnailCacheServiceTests
{
    [Fact]
    public async Task GetThumbnailAsync_InvalidUrl_DoesNotCallHttp()
    {
        using var directory = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var result = await service.GetThumbnailAsync("not a URL");

        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

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
        using var service = new ThumbnailCacheService(client, directory.Path, maxDownloadBytes: 10);

        var result = await service.GetThumbnailAsync("https://example.com/large.jpg");

        Assert.Null(result);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task GetThumbnailAsync_WebPDownloadIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(webp)
        }));
        using var client = new HttpClient(handler);
        using var service = new ThumbnailCacheService(client, directory.Path);

        var result = await service.GetThumbnailAsync("https://example.com/image.jpg");

        Assert.Null(result);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
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
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
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
