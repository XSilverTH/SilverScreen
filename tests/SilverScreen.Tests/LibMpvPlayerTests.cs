using System.Collections.Concurrent;
using System.Reflection;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class LibMpvPlayerTests
{
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
    public void SeekAbsoluteDispatchesExactAndKeyframeCommands()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.SeekAbsolute(42.5);
        player.SeekAbsolute(100.25, false);

        Assert.True(SpinWait.SpinUntil(() => native.Commands.Count >= 2, TimeSpan.FromSeconds(2)));
        Assert.Contains("seek|42.5|absolute+exact", native.Commands);
        Assert.Contains("seek|100.25|absolute+keyframes", native.Commands);
    }

    [Fact]
    public void SeekRelativeDispatchesRelativeExactCommand()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.SeekRelative(-10);

        Assert.True(SpinWait.SpinUntil(() => native.Commands.Count >= 1, TimeSpan.FromSeconds(2)));
        Assert.Contains("seek|-10|relative+exact", native.Commands);
    }

    [Fact]
    public void PlaylistCommandsDispatchCorrectMpvArguments()
    {
        using var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.PlayPlaylistIndex(3);
        player.RemovePlaylistItem(1);
        player.MovePlaylistItem(0, 2);
        player.MovePlaylistItem(4, 1);
        player.AppendPlaylistItem("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.True(SpinWait.SpinUntil(() => native.Commands.Count >= 5, TimeSpan.FromSeconds(2)));
        Assert.Contains("playlist-play-index|3", native.Commands);
        Assert.Contains("playlist-remove|1", native.Commands);
        Assert.Contains("playlist-move|0|3", native.Commands);
        Assert.Contains("playlist-move|4|1", native.Commands);
        Assert.Contains("loadfile|https://www.youtube.com/watch?v=dQw4w9WgXcQ|append-play", native.Commands);
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