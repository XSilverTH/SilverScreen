using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Features.Thumbnails;
using Xunit;

namespace SilverScreen.Tests;

public sealed class ThumbnailCacheServiceTests
{
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
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Prevent failures in cleanup from crashing tests
            }
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public int CallCount { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return await _handler(request, cancellationToken);
        }
    }

    [Fact]
    public void CreateCacheKey_SameUrl_ReturnsStableKey()
    {
        // Arrange
        const string url = "https://example.com/images/thumb.png";

        // Act
        var key1 = ThumbnailCacheService.CreateCacheKey(url);
        var key2 = ThumbnailCacheService.CreateCacheKey(url);

        // Assert
        Assert.Equal(key1, key2);
        Assert.False(string.IsNullOrWhiteSpace(key1));
        Assert.Equal(64, key1.Length);
    }

    [Fact]
    public void CreateCacheKey_DifferentUrls_ReturnsDifferentKeys()
    {
        // Arrange
        const string url1 = "https://example.com/images/thumb1.png";
        const string url2 = "https://example.com/images/thumb2.png";

        // Act
        var key1 = ThumbnailCacheService.CreateCacheKey(url1);
        var key2 = ThumbnailCacheService.CreateCacheKey(url2);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetCachePathForUrl_SameUrl_ReturnsStablePath()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/thumb.png";

        // Act
        var path1 = service.GetCachePathForUrl(url);
        var path2 = service.GetCachePathForUrl(url);

        // Assert
        Assert.Equal(path1, path2);
        Assert.StartsWith(tempDir.Path, path1);
        Assert.EndsWith(".png", path1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/image.jpg")]
    [InlineData("file:///C:/image.jpg")]
    public async Task GetThumbnailAsync_InvalidOrNonHttpUrl_ReturnsNullAndDoesNotCallHttp(string url)
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            throw new InvalidOperationException("HTTP handler should not be called for invalid URLs.");
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_CachedFileExists_ReusesFileWithoutCallingHttp()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            throw new InvalidOperationException("HTTP handler should not be called when file is cached.");
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/cached.jpg";

        // Pre-create the cached file
        var cachePath = service.GetCachePathForUrl(url);
        Directory.CreateDirectory(tempDir.Path);
        await File.WriteAllBytesAsync(cachePath, "fake image bytes"u8.ToArray());

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(cachePath, result.LocalPath);
        Assert.True(result.WasCacheHit);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GetThumbnailAsync_FailedHttpResponse_ReturnsNullAndDoesNotCreateCacheFile(HttpStatusCode statusCode)
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(statusCode);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/failed.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.False(File.Exists(cachePath));

        if (Directory.Exists(tempDir.Path))
        {
            Assert.Empty(Directory.GetFiles(tempDir.Path));
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_HttpException_ReturnsNullAndDoesNotCreateCacheFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            throw new HttpRequestException("Network failure");
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/error.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.False(File.Exists(cachePath));

        if (Directory.Exists(tempDir.Path))
        {
            Assert.Empty(Directory.GetFiles(tempDir.Path));
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_ContentLengthExceedsLimit_ReturnsNullAndDoesNotCreateCacheFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        const long maxBytes = 100;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(new byte[maxBytes + 1]);
            Assert.Equal(maxBytes + 1, response.Content.Headers.ContentLength);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path, maxDownloadBytes: maxBytes);
        const string url = "https://example.com/images/oversized-header.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.False(File.Exists(cachePath));

        if (Directory.Exists(tempDir.Path))
        {
            Assert.Empty(Directory.GetFiles(tempDir.Path));
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_ContentStreamExceedsLimitDuringDownload_ReturnsNullAndDoesNotCreateCacheFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        const long maxBytes = 10;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            var oversizedData = new byte[maxBytes + 5];
            var stream = new MemoryStream(oversizedData);
            response.Content = new StreamContent(stream);
            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path, maxDownloadBytes: maxBytes);
        const string url = "https://example.com/images/oversized-stream.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.False(File.Exists(cachePath));

        if (Directory.Exists(tempDir.Path))
        {
            Assert.Empty(Directory.GetFiles(tempDir.Path));
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_SuccessfulDownload_SavesFileToCacheAndReturnsCacheHitFalse()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var imageData = "real image bytes"u8.ToArray();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(imageData);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/success.png";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.Equal(cachePath, result.LocalPath);
        Assert.True(File.Exists(cachePath));

        var savedBytes = await File.ReadAllBytesAsync(cachePath);
        Assert.Equal(imageData, savedBytes);
    }

    [Fact]
    public async Task GetThumbnailAsync_VideoIsShort_ReturnsNull()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var handler = new FakeHttpMessageHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        var video = new VideoSummary("id", "title", "channel", TimeSpan.FromMinutes(1), "https://example.com/thumb.jpg", IsShort: true);

        // Act
        var result = await service.GetThumbnailAsync(video);

        // Assert
        Assert.Null(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_VideoIsNotShort_FetchesThumbnail()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var imageData = "image"u8.ToArray();
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(imageData);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        var video = new VideoSummary("id", "title", "channel", TimeSpan.FromMinutes(1), "https://example.com/thumb.jpg", IsShort: false);

        // Act
        var result = await service.GetThumbnailAsync(video);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);

        var cachePath = service.GetCachePathForUrl(video.ThumbnailUrl);
        Assert.Equal(cachePath, result.LocalPath);
        Assert.True(File.Exists(cachePath));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetThumbnailAsync_RequestsIncludeAcceptHeaderPreferringJpegAndPng()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        string? acceptHeader = null;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            if (req.Headers.TryGetValues("Accept", out var values))
            {
                acceptHeader = string.Join(", ", values);
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent("image"u8.ToArray());
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/accept.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, handler.CallCount);
        Assert.NotNull(acceptHeader);
        Assert.Contains("image/jpeg", acceptHeader);
        Assert.Contains("image/png", acceptHeader);
        Assert.StartsWith("image/jpeg, image/png", acceptHeader);
    }

    [Fact]
    public async Task GetThumbnailAsync_CachedWebPFileExists_DiscardsAndRedownloads()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var webpBytes = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        var validImageBytes = "valid-jpeg-bytes"u8.ToArray();

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(validImageBytes);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/cached-webp.jpg";

        var cachePath = service.GetCachePathForUrl(url);
        Directory.CreateDirectory(tempDir.Path);
        await File.WriteAllBytesAsync(cachePath, webpBytes);

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);
        Assert.Equal(1, handler.CallCount);

        Assert.True(File.Exists(cachePath));
        var savedBytes = await File.ReadAllBytesAsync(cachePath);
        Assert.Equal(validImageBytes, savedBytes);
    }

    [Fact]
    public async Task GetThumbnailAsync_WebPDownload_ReturnsNullAndDoesNotLeaveCacheFile()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        var webpBytes = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(webpBytes);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);
        const string url = "https://example.com/images/downloaded-webp.jpg";

        // Act
        var result = await service.GetThumbnailAsync(url);

        // Assert
        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);

        var cachePath = service.GetCachePathForUrl(url);
        Assert.False(File.Exists(cachePath));

        if (Directory.Exists(tempDir.Path))
        {
            Assert.Empty(Directory.GetFiles(tempDir.Path));
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_YouTubeHq720UrlWithQueryParameters_NormalizesToMaxresDefaultAndCachesOriginalUrl()
    {
        // Arrange
        using var tempDir = new TemporaryDirectory();
        const string originalUrl = "https://i.ytimg.com/vi/dQw4w9WgXcQ/hq720.jpg?sqp=-oaymwEXCNUGEOADIAQqCwj81Y79Bw==&rs=AOn4CLD_U5rS3pL1z9W1kO3-n1u8H5g1uA";
        const string expectedRequestUrl = "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg";
        var imageData = "fake-jpeg-bytes"u8.ToArray();

        string? requestedUrl = null;
        var handler = new FakeHttpMessageHandler((req, ct) =>
        {
            requestedUrl = req.RequestUri?.AbsoluteUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imageData)
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        using var service = new ThumbnailCacheService(httpClient, tempDir.Path);

        // Act
        var result = await service.GetThumbnailAsync(originalUrl);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.WasCacheHit);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(expectedRequestUrl, requestedUrl);

        var expectedCachePath = service.GetCachePathForUrl(originalUrl);
        Assert.Equal(expectedCachePath, result.LocalPath);
        Assert.True(File.Exists(expectedCachePath));

        var savedBytes = await File.ReadAllBytesAsync(expectedCachePath);
        Assert.Equal(imageData, savedBytes);
    }

}
