using System.Reflection;
using System.Collections.Concurrent;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.Views.Player;

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
        Assert.Contains(("ytdl-raw-options",
            "cookies=/tmp/cookies.txt,write-subs=,write-auto-subs=,sub-langs=all,sub-format=vtt,mark-watched="),
            native.StringProperties);
        Assert.Contains(("ytdl-format", "bestvideo[height<=720]+bestaudio/best[height<=720]"), native.StringProperties);
        Assert.Contains(("keep-open", "yes"), native.StringProperties);
    }

    [Fact]
    public void LiveTelemetrySuppressesYtDlpMarkWatched()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.Load(new PlaybackRequest([Video("abc123_X-yZ")]),
            new AppPreferences { MarkWatchedVideos = true, YouTubePlaybackTelemetryEnabled = true },
            "/tmp/cookies.txt");

        Assert.True(SpinWait.SpinUntil(() => native.StringProperties.Any(property =>
            property.Name == "ytdl-raw-options"), TimeSpan.FromSeconds(2)));
        Assert.Contains(("ytdl-raw-options",
            "cookies=/tmp/cookies.txt,write-subs=,write-auto-subs=,sub-langs=all,sub-format=vtt"),
            native.StringProperties);
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
    public void LoadAssumesPlaybackStartsUnpausedUntilMpvReportsOtherwise()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var states = new ConcurrentQueue<LibMpvPlaybackState>();
        player.StateChanged += (_, state) => states.Enqueue(state);

        player.Load(new PlaybackRequest([Video("abc123_X-yZ")]), new AppPreferences(), null);

        Assert.True(SpinWait.SpinUntil(
            () => states.Any(state => state.IsLoading && !state.IsPaused), TimeSpan.FromSeconds(2)));
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
        Assert.Equal(("ytdl-raw-options", "write-subs=,write-auto-subs=,sub-langs=all,sub-format=vtt"),
            native.StringProperties.Last(property => property.Name == "ytdl-raw-options"));
    }

    [Fact]
    public void EndOfFinalVideoKeepsLoadedMediaAvailableForReplay()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var stateField = typeof(LibMpvPlayer).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
        var handleEndFile = typeof(LibMpvPlayer).GetMethod("HandleEndFile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateField);
        Assert.NotNull(handleEndFile);
        stateField.SetValue(player, new LibMpvPlaybackState(0, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3),
            true, false, 100, 1, true, true, false, [], []));

        handleEndFile.Invoke(player, [new LibMpvEventEndFile(LibMpvEndFileReason.Eof, 0, 0, 0, 0)]);

        var state = Assert.IsType<LibMpvPlaybackState>(stateField.GetValue(player));
        Assert.True(state.HasMedia);
        Assert.Equal(TimeSpan.FromMinutes(3), state.Position);
    }

    [Fact]
    public void LoadPreventsAutomaticAdvanceWhenConfigured()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.Load(new PlaybackRequest([Video("abc123_X-yZ")]),
            new AppPreferences { AutoAdvanceNextVideo = false }, null);

        Assert.True(SpinWait.SpinUntil(
            () => native.StringProperties.Contains(("keep-open", "always")), TimeSpan.FromSeconds(2)));
    }


    [Fact]
    public void SubtitleSelectionUsesMpvSubtitleIdsAndSupportsTurningSubtitlesOff()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.SelectSubtitleTrack(42);
        player.SelectSubtitleTrack(0);

        Assert.True(SpinWait.SpinUntil(() => native.StringProperties.Count(property => property.Name == "sid") == 2,
            TimeSpan.FromSeconds(2)));
        Assert.Contains(("sid", "42"), native.StringProperties);
        Assert.Contains(("sid", "no"), native.StringProperties);
    }

    [Fact]
    public void FileLoadedPublishesNamedChaptersFromMpv()
    {
        using var native = new RecordingNative();
        native.ReadProperties["chapter-list/count"] = "3";
        native.ReadProperties["chapter-list/0/time"] = "0";
        native.ReadProperties["chapter-list/0/title"] = "Introduction";
        native.ReadProperties["chapter-list/1/time"] = "42.5";
        native.ReadProperties["chapter-list/1/title"] = "The important part";
        native.ReadProperties["chapter-list/2/time"] = "90";
        var states = new ConcurrentQueue<LibMpvPlaybackState>();
        using var player = new LibMpvPlayer(native, action => action());
        player.StateChanged += (_, state) => states.Enqueue(state);
        var handleFileLoaded = typeof(LibMpvPlayer).GetMethod("HandleFileLoaded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handleFileLoaded);

        handleFileLoaded.Invoke(player, null);

        var chapters = Assert.Single(states).Chapters;
        Assert.Collection(chapters,
            chapter =>
            {
                Assert.Equal(TimeSpan.Zero, chapter.Start);
                Assert.Equal("Introduction", chapter.Title);
            },
            chapter =>
            {
                Assert.Equal(TimeSpan.FromSeconds(42.5), chapter.Start);
                Assert.Equal("The important part", chapter.Title);
            },
            chapter =>
            {
                Assert.Equal(TimeSpan.FromSeconds(90), chapter.Start);
                Assert.Equal("Chapter 3", chapter.Title);
            });
    }




    [Fact]
    public void KeyboardTransportCommandsUseExpectedMpvCommands()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.ToggleMute();
        player.StepFrame(true);
        player.StepFrame(false);
        player.AdjustVolume(5);
        player.AdjustVolume(-5);
        player.MovePlaylist(true);
        player.MovePlaylist(false);

        Assert.True(SpinWait.SpinUntil(() => native.Commands.Count >= 7, TimeSpan.FromSeconds(2)));
        Assert.Contains("cycle|mute", native.Commands);
        Assert.Contains("frame-step", native.Commands);
        Assert.Contains("frame-back-step", native.Commands);
        Assert.Contains("add|volume|5", native.Commands);
        Assert.Contains("add|volume|-5", native.Commands);
        Assert.Contains("playlist-next", native.Commands);
        Assert.Contains("playlist-prev", native.Commands);
    }


    private static VideoSummary Video(string id)
    {
        return new VideoSummary(id, id, "Channel", TimeSpan.FromMinutes(3), "", false);
    }

    private sealed class RecordingNative : ILibMpvNativeApi
    {
        public ConcurrentBag<string> Commands { get; } = [];
        public ConcurrentBag<(string Name, double Value)> DoubleProperties { get; } = [];
        public ConcurrentQueue<(string Name, string Value)> StringProperties { get; } = [];
        public Dictionary<string, string> ReadProperties { get; } = [];
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
            StringProperties.Enqueue((name, value));
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

        public string? GetPropertyString(nint handle, string name)
        {
            return ReadProperties.GetValueOrDefault(name);
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