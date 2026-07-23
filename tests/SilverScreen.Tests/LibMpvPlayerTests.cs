using System.Collections.Concurrent;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class LibMpvPlayerTests
{
    [Fact]
    public void LoadAndTransportCommandsUseTheExpectedLibMpvSemantics()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var request = new PlaybackRequest([
            Video("abc123_X-yZ"),
            Video("dQw4w9WgXcQ"),
            Video("M7lc1UVf-VE")
        ]);

        player.Load(request, new AppPreferences { VideoQuality = "720p", MarkWatchedVideos = true },
            "/tmp/cookies.txt");
        player.SeekRelative(-10);
        player.SeekRelative(10);
        player.SeekAbsolute(42);
        player.SetVolume(150);
        player.SetSpeed(1.5);

        Assert.True(SpinWait.SpinUntil(() => native.Commands.Count >= 6, TimeSpan.FromSeconds(2)));
        Assert.Contains("loadfile|https://www.youtube.com/watch?v=abc123_X-yZ|replace", native.Commands);
        Assert.Contains("loadfile|https://www.youtube.com/watch?v=dQw4w9WgXcQ|append-play", native.Commands);
        Assert.Contains("loadfile|https://www.youtube.com/watch?v=M7lc1UVf-VE|append-play", native.Commands);
        Assert.Contains("seek|-10|relative+exact", native.Commands);
        Assert.Contains("seek|10|relative+exact", native.Commands);
        Assert.Contains("seek|42|absolute+exact", native.Commands);
        Assert.Contains(("volume", 100d), native.DoubleProperties);
        Assert.Contains(("speed", 1.5d), native.DoubleProperties);
        Assert.Contains(("ytdl-raw-options", "cookies=/tmp/cookies.txt,mark-watched="), native.StringProperties);
        Assert.Contains(("ytdl-format", "bestvideo[height<=720]+bestaudio/best[height<=720]"), native.StringProperties);
    }

    [Fact]
    public void LoadPublishesLoadingState()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var states = new ConcurrentQueue<LibMpvPlaybackState>();
        player.StateChanged += (_, state) => states.Enqueue(state);

        player.Load(new PlaybackRequest([Video("abc123_X-yZ")]), new AppPreferences(), null);

        Assert.True(SpinWait.SpinUntil(() => states.Any(state => state.IsLoading), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void CookieLessLoadClearsPreviousYtdlRawOptions()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var request = new PlaybackRequest([Video("abc123_X-yZ")]);

        player.Load(request, new AppPreferences(), "/tmp/cookies.txt");
        player.Load(request, new AppPreferences(), null);

        Assert.True(SpinWait.SpinUntil(
            () => native.StringProperties.Count(property => property.Name == "ytdl-raw-options") >= 2,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(("ytdl-raw-options", string.Empty),
            native.StringProperties.Last(property => property.Name == "ytdl-raw-options"));
    }

    [Fact]
    public void NativeLoaderCreatesAndDestroysAnMpvHandle()
    {
        using var native = new LibMpvNative();
        Assert.True(native.IsLoaded, native.AvailabilityError);

        var handle = native.Create();
        Assert.NotEqual(0, handle);
        native.Destroy(handle);
    }

    private static VideoSummary Video(string id)
    {
        return new VideoSummary(id, id, "Channel", TimeSpan.FromMinutes(3), "", false);
    }

    private sealed class RecordingNative : ILibMpvNativeApi
    {
        public ConcurrentBag<string> Commands { get; } = [];
        public ConcurrentBag<(string Name, double Value)> DoubleProperties { get; } = [];
        public ConcurrentBag<(string Name, string Value)> StringProperties { get; } = [];
        public bool IsAvailable => true;
        public string? AvailabilityError => null;

        public nint Create()
        {
            return 1;
        }

        public int SetOptionString(nint handle, string name, string value)
        {
            return 0;
        }

        public int Initialize(nint handle)
        {
            return 0;
        }

        public int ObserveProperty(nint handle, ulong replyUserdata, string name, LibMpvFormat format)
        {
            return 0;
        }

        public int SetPropertyString(nint handle, string name, string value)
        {
            StringProperties.Add((name, value));
            return 0;
        }

        public int SetPropertyDouble(nint handle, string name, double value)
        {
            DoubleProperties.Add((name, value));
            return 0;
        }

        public int SetPropertyFlag(nint handle, string name, bool value)
        {
            return 0;
        }

        public int SetPropertyInt64(nint handle, string name, long value)
        {
            return 0;
        }

        public int Command(nint handle, params string[] arguments)
        {
            Commands.Add(string.Join('|', arguments));
            return 0;
        }

        public LibMpvEvent WaitEvent(nint handle, double timeout)
        {
            return new LibMpvEvent((int)LibMpvEventId.Shutdown, 0, 0, 0);
        }

        public void Wakeup(nint handle)
        {
        }

        public string ErrorString(int error)
        {
            return $"error {error}";
        }

        public int CreateRenderContext(out nint context, nint handle)
        {
            context = 2;
            return 0;
        }

        public void SetRenderUpdateCallback(nint context, nint callback, nint callbackData)
        {
        }

        public int GetFramebufferBinding()
        {
            return 0;
        }

        public int Render(nint context, int framebuffer, int width, int height)
        {
            return 0;
        }

        public void FreeRenderContext(nint context)
        {
        }

        public void Destroy(nint handle)
        {
        }

        public void Dispose()
        {
        }
    }
}