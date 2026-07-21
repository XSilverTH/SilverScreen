using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YtDlpHomeClientTests
{
    private const string FakeCookieContent = """
                                             # Netscape HTTP Cookie File
                                             .youtube.com	TRUE	/	TRUE	2147483647	SID	fake-session-value-123
                                             .youtube.com	TRUE	/	TRUE	2147483647	HSID	fake-session-value-456
                                             """;

    [Fact]
    public async Task GetHomeFeedAsync_WithNoManualSession_ReturnsRequiresAuthentication()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, "non-existent-executable");

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresAuthentication);
        Assert.Equal("Authentication session not found.", result.StatusMessage);
        Assert.Empty(result.Videos);
    }

    [Fact]
    public async Task GetHomeFeedAsync_WithSubprocessExitCodeNonZero_ReturnsSafeFailureWithoutSecretLeakage()
    {
        // Arrange
        using var tempScript = new TempScript();
        tempScript.SetExitCode(5);
        tempScript.SetOutput(string.Empty, "ERROR: Some private key / secret / credentials failed");

        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, tempScript.Path);

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.RequiresAuthentication);
        Assert.Equal("yt-dlp process exited with error code 5.", result.StatusMessage);

        // Ensure standard error containing potential private info or secret is NOT leaked
        Assert.DoesNotContain("private key", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentials", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fake-session-value-123", result.StatusMessage);
        Assert.DoesNotContain(tempScript.StdoutPath, result.StatusMessage);
        Assert.Empty(result.Videos);
    }

    [Fact]
    public async Task GetHomeFeedAsync_WithMissingSubprocessExecutable_ReturnsCleanFailure()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, "non-existent-executable-file-path");

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.RequiresAuthentication);
        Assert.Equal("Exception while starting yt-dlp process.", result.StatusMessage);
        Assert.Empty(result.Videos);
    }

    [Fact]
    public async Task GetHomeFeedAsync_ParsesValidPlaylistJsonAndPerformsThumbnailSelection()
    {
        // Arrange
        using var tempScript = new TempScript();
        var playlistJson = """
                           {
                             "entries": [
                               {
                                 "id": "video111111",
                                 "title": "Video One",
                                 "uploader": "Uploader One",
                                 "duration": 120,
                                 "upload_date": "20260715",
                                "timestamp": 1784073600,
                                 "thumbnails": [
                                   { "url": "https://thumb.url/1_low", "preference": 5, "width": 1280, "height": 720 },
                                   { "url": "https://thumb.url/1_high", "preference": 10, "width": 320, "height": 180 }
                                 ]
                               },
                               {
                                 "id": "video222222",
                                 "title": "Video Two",
                                 "uploader": "Uploader Two",
                                 "duration": "180",
                                 "upload_date": "20260714",
                                 "thumbnails": [
                                   { "url": "https://thumb.url/2_small", "preference": 0, "width": 320, "height": 180 },
                                   { "url": "https://thumb.url/2_large", "preference": 0, "width": 1920, "height": 1080 }
                                 ]
                               },
                               {
                                 "id": "video333333",
                                 "title": "Video Three",
                                 "uploader": "Uploader Three",
                                 "duration": 300,
                                 "upload_date": "20260713",
                                 "thumbnail": "https://thumb.url/3_fallback"
                               }
                             ]
                           }
                           """;
        tempScript.SetOutput(playlistJson);

        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, tempScript.Path);

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Recommendations loaded successfully.", result.StatusMessage);
        Assert.Equal(3, result.Videos.Count);

        var v1 = result.Videos[0];
        Assert.Equal("video111111", v1.Id);
        Assert.Equal("Video One", v1.Title);
        Assert.Equal("Uploader One", v1.ChannelName);
        Assert.Equal(TimeSpan.FromSeconds(120), v1.Duration);
        Assert.Equal("https://thumb.url/1_high", v1.ThumbnailUrl); // preference 10 beats 5
        Assert.Equal("https://www.youtube.com/watch?v=video111111", v1.WatchUrl);
        Assert.Equal(new DateOnly(2026, 7, 15), v1.ApproximateUploadDate);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784073600), v1.PublishedAt);

        var v2 = result.Videos[1];
        Assert.Equal("video222222", v2.Id);
        Assert.Equal("Video Two", v2.Title);
        Assert.Equal("Uploader Two", v2.ChannelName);
        Assert.Equal(TimeSpan.FromSeconds(180), v2.Duration);
        Assert.Equal("https://thumb.url/2_large", v2.ThumbnailUrl); // larger area wins when preferences are equal
        Assert.Equal(new DateOnly(2026, 7, 14), v2.ApproximateUploadDate);

        var v3 = result.Videos[2];
        Assert.Equal("video333333", v3.Id);
        Assert.Equal("Video Three", v3.Title);
        Assert.Equal("Uploader Three", v3.ChannelName);
        Assert.Equal(TimeSpan.FromSeconds(300), v3.Duration);
        Assert.Equal("https://thumb.url/3_fallback", v3.ThumbnailUrl); // fallback to thumbnail property
        Assert.Equal(new DateOnly(2026, 7, 13), v3.ApproximateUploadDate);
    }

    [Fact]
    public async Task GetHomeFeedAsync_FiltersShortsAndUsesSharedTolerantMetadataMapping()
    {
        // Arrange
        using var tempScript = new TempScript();
        var playlistJson = """
                           {
                             "entries": [
                               {
                                 "id": "validVideo",
                                 "title": "A Normal Video",
                                 "uploader": "Channel Name",
                                 "duration": 600,
                                 "upload_date": "not-a-date",
                                 "thumbnail": "https://thumb.url/valid"
                               },
                               {
                                 "id": "shortVideo1",
                                 "title": "Short Video",
                                 "is_short": true
                               },
                               {
                                 "id": "shortVideo2",
                                 "title": "Another Short Video",
                                 "webpage_url": "https://www.youtube.com/shorts/shortVideo2"
                               },
                               {
                                 "id": "shortVideo3",
                                 "title": "Title with #shorts hashtag",
                                 "thumbnail": "https://thumb.url/short3"
                               },
                               {
                                 "title": "Missing ID video"
                               },
                               {
                                 "id": "missingTitleVideo"
                               }
                             ]
                           }
                           """;
        tempScript.SetOutput(playlistJson);

        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, tempScript.Path);

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Videos,
            video =>
            {
                Assert.Equal("validVideo", video.Id);
                Assert.Equal("A Normal Video", video.Title);
                Assert.Null(video.ApproximateUploadDate);
            },
            video =>
            {
                Assert.Equal("missingTitleVideo", video.Id);
                Assert.Equal("Untitled YouTube video", video.Title);
                Assert.Equal("YouTube", video.ChannelName);
            });
    }

    [Fact]
    public async Task GetHomeFeedAsync_CookieBackedReturnsEmpty_RetriesCookieFreeAndSucceeds()
    {
        // Arrange
        using var tempScript = new FallbackTempScript();

        var emptyPlaylist = "{\"entries\": []}";
        var validPlaylist = """
                            {
                              "entries": [
                                {
                                  "id": "fallback_video_id",
                                  "title": "Fallback Recommendation",
                                  "uploader": "Fallback Channel",
                                  "duration": 420,
                                  "thumbnail": "https://thumb.url/fallback"
                                }
                              ]
                            }
                            """;
        tempScript.SetOutputs(emptyPlaylist, validPlaylist);

        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, tempScript.Path);

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Public recommendations are displayed.", result.StatusMessage);
        Assert.False(result.RequiresAuthentication);

        var video = Assert.Single(result.Videos);
        Assert.Equal("fallback_video_id", video.Id);
        Assert.Equal("Fallback Recommendation", video.Title);
        Assert.Equal("Fallback Channel", video.ChannelName);
        Assert.Null(video.ApproximateUploadDate);

        // Assert that both invocation modes occurred in order
        var log = tempScript.GetLog();
        Assert.Equal(2, log.Length);
        Assert.Contains("--cookies", log[0]);
        Assert.DoesNotContain("--cookies", log[1]);
    }

    [Fact]
    public async Task GetHomeFeedAsync_CookieBackedExitsNonZero_ReturnsFailureWithoutRetry()
    {
        // Arrange
        using var tempScript = new FallbackTempScript();
        tempScript.SetExitCode(3);
        tempScript.SetOutputs("{\"entries\": []}", "{\"entries\": []}");

        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(FakeCookieContent, SessionCookieFormat.NetscapeCookiesText);

        var cookieFileProvider = new TemporaryCookieFileProvider(sessionService);
        var client = new YtDlpHomeClient(sessionService, cookieFileProvider, tempScript.Path);

        // Act
        var result = await client.GetHomeFeedAsync(cancellationToken: CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("yt-dlp process exited with error code 3.", result.StatusMessage);
        Assert.Empty(result.Videos);

        // Assert that only the cookie-backed invocation occurred, and no retry
        var log = tempScript.GetLog();
        var invocation = Assert.Single(log);
        Assert.Contains("--cookies", invocation);
    }

    private sealed class FallbackTempScript : IDisposable
    {
        public FallbackTempScript()
        {
            var id = Guid.NewGuid().ToString("N");
            LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-log-{id}.txt");
            CookieStdoutPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-cookie-stdout-{id}.txt");
            NoCookieStdoutPath =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-nocookie-stdout-{id}.txt");
            ExitCodePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-exitcode-{id}.txt");

            // Default exit code to 0
            File.WriteAllText(ExitCodePath, "0");

            if (OperatingSystem.IsWindows())
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fake-yt-dlp-{id}.cmd");
                var batchContent = $"""
                                    @echo off
                                    if exist "{ExitCodePath}" (
                                      set /p EXIT_CODE=<{ExitCodePath}
                                    ) else (
                                      set EXIT_CODE=0
                                    )

                                    set "HAS_COOKIES=0"
                                    for %%x in (%*) do if "%%~x"=="--cookies" set "HAS_COOKIES=1"

                                    if "%HAS_COOKIES%"=="1" (
                                      echo --dump-single-json --flat-playlist --skip-download --extractor-args youtubetab:approximate_date --cookies [COOKIE_FILE] :ytrec >> "{LogPath}"
                                      if exist "{CookieStdoutPath}" (
                                        type "{CookieStdoutPath}"
                                      )
                                    ) else (
                                      echo --dump-single-json --flat-playlist --skip-download --extractor-args youtubetab:approximate_date :ytrec >> "{LogPath}"
                                      if exist "{NoCookieStdoutPath}" (
                                        type "{NoCookieStdoutPath}"
                                      )
                                    )

                                    exit /b %EXIT_CODE%
                                    """;
                File.WriteAllText(Path, batchContent);
            }
            else
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fake-yt-dlp-{id}");
                var shellContent = $"""
                                    #!/bin/sh
                                    if [ -f "{ExitCodePath}" ]; then
                                      exit_code=$(cat "{ExitCodePath}")
                                    else
                                      exit_code=0
                                    fi

                                    has_cookies=0
                                    for arg in "$@"; do
                                      if [ "$arg" = "--cookies" ]; then
                                        has_cookies=1
                                      fi
                                    done

                                    if [ "$has_cookies" -eq 1 ]; then
                                      echo "--dump-single-json --flat-playlist --skip-download --extractor-args youtubetab:approximate_date --cookies [COOKIE_FILE] :ytrec" >> "{LogPath}"
                                      if [ -f "{CookieStdoutPath}" ]; then
                                        cat "{CookieStdoutPath}"
                                      fi
                                    else
                                      echo "--dump-single-json --flat-playlist --skip-download --extractor-args youtubetab:approximate_date :ytrec" >> "{LogPath}"
                                      if [ -f "{NoCookieStdoutPath}" ]; then
                                        cat "{NoCookieStdoutPath}"
                                      fi
                                    fi

                                    exit "$exit_code"
                                    """;
                File.WriteAllText(Path, shellContent);
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string Path { get; }
        public string LogPath { get; }
        public string CookieStdoutPath { get; }
        public string NoCookieStdoutPath { get; }
        public string ExitCodePath { get; }

        public void Dispose()
        {
            TryDelete(Path);
            TryDelete(LogPath);
            TryDelete(CookieStdoutPath);
            TryDelete(NoCookieStdoutPath);
            TryDelete(ExitCodePath);
        }

        public void SetExitCode(int exitCode)
        {
            File.WriteAllText(ExitCodePath, exitCode.ToString());
        }

        public void SetOutputs(string cookieStdout, string noCookieStdout)
        {
            File.WriteAllText(CookieStdoutPath, cookieStdout);
            File.WriteAllText(NoCookieStdoutPath, noCookieStdout);
        }

        public string[] GetLog()
        {
            if (File.Exists(LogPath)) return File.ReadAllLines(LogPath);

            return Array.Empty<string>();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Ignore cleanup exceptions
            }
        }
    }

    private sealed class TempScript : IDisposable
    {
        public TempScript()
        {
            var id = Guid.NewGuid().ToString("N");
            StdoutPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-stdout-{id}.txt");
            StderrPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-stderr-{id}.txt");
            ExitCodePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"yt-dlp-exitcode-{id}.txt");

            // Set default exit code to 0
            File.WriteAllText(ExitCodePath, "0");

            if (OperatingSystem.IsWindows())
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fake-yt-dlp-{id}.cmd");
                var batchContent = $"""
                                    @echo off
                                    if exist "{ExitCodePath}" (
                                      set /p EXIT_CODE=<{ExitCodePath}
                                    ) else (
                                      set EXIT_CODE=0
                                    )

                                    set "ARG_COUNT=0"
                                    for %%x in (%*) do set /a ARG_COUNT+=1
                                    if not "%ARG_COUNT%"=="8" (
                                      echo Error: expected 8 arguments, got %ARG_COUNT% >&2
                                      exit /b 2
                                    )

                                    if not "%~1"=="--dump-single-json" goto bad_args
                                    if not "%~2"=="--flat-playlist" goto bad_args
                                    if not "%~3"=="--skip-download" goto bad_args
                                    if not "%~4"=="--extractor-args" goto bad_args
                                    if not "%~5"=="youtubetab:approximate_date" goto bad_args
                                    if not "%~6"=="--cookies" goto bad_args
                                    if not "%~8"==":ytrec" goto bad_args
                                    goto check_cookies

                                    :bad_args
                                    echo Error: invalid arguments >&2
                                    exit /b 3

                                    :check_cookies
                                    if not exist "%~7" (
                                      echo Error: cookies file not found >&2
                                      exit /b 4
                                    )

                                    if exist "{StderrPath}" (
                                      type "{StderrPath}" >&2
                                    )

                                    if exist "{StdoutPath}" (
                                      type "{StdoutPath}"
                                    )

                                    exit /b %EXIT_CODE%
                                    """;
                File.WriteAllText(Path, batchContent);
            }
            else
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fake-yt-dlp-{id}");
                var shellContent = $"""
                                    #!/bin/sh
                                    if [ -f "{ExitCodePath}" ]; then
                                      exit_code=$(cat "{ExitCodePath}")
                                    else
                                      exit_code=0
                                    fi

                                    if [ "$#" -ne 8 ]; then
                                      echo "Error: expected 8 arguments, got $#" >&2
                                      exit 2
                                    fi

                                    if [ "$1" != "--dump-single-json" ] || [ "$2" != "--flat-playlist" ] || [ "$3" != "--skip-download" ] || [ "$4" != "--extractor-args" ] || [ "$5" != "youtubetab:approximate_date" ] || [ "$6" != "--cookies" ] || [ "$8" != ":ytrec" ]; then
                                      echo "Error: invalid arguments: $@" >&2
                                      exit 3
                                    fi

                                    if [ ! -f "$7" ]; then
                                      echo "Error: cookies file not found at $7" >&2
                                      exit 4
                                    fi

                                    if [ -f "{StderrPath}" ]; then
                                      cat "{StderrPath}" >&2
                                    fi

                                    if [ -f "{StdoutPath}" ]; then
                                      cat "{StdoutPath}"
                                    fi

                                    exit "$exit_code"
                                    """;
                File.WriteAllText(Path, shellContent);
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string Path { get; }
        public string StdoutPath { get; }
        public string StderrPath { get; }
        public string ExitCodePath { get; }

        public void Dispose()
        {
            TryDelete(Path);
            TryDelete(StdoutPath);
            TryDelete(StderrPath);
            TryDelete(ExitCodePath);
        }

        public void SetOutput(string stdout, string stderr = "")
        {
            File.WriteAllText(StdoutPath, stdout);
            File.WriteAllText(StderrPath, stderr);
        }

        public void SetExitCode(int exitCode)
        {
            File.WriteAllText(ExitCodePath, exitCode.ToString());
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Ignore clean up exceptions
            }
        }
    }
}