using System.Collections.Concurrent;
using System.Reflection;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Tests.Player;

public sealed class LibMpvPlayerTests
{
    [Fact]
    public void LoadAssumesPlaybackStartsUnpausedUntilMpvReportsOtherwise()
    {
        var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());
        var states = new ConcurrentQueue<LibMpvPlaybackState>();
        player.StateChanged += (_, state) => states.Enqueue(state);

        player.Load(new PlaybackRequest([Video("abc123_X-yZ")]), new AppPreferences(), null);

        Assert.True(SpinWait.SpinUntil(
            () => states.Any(state => state is { IsLoading: true, IsPaused: false }), TimeSpan.FromSeconds(2)));
    }


    [Fact]
    public void SubtitleSelectionUsesMpvSubtitleIdsAndSupportsTurningSubtitlesOff()
    {
        var native = new RecordingNative();
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
        var native = new RecordingNative();
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
        var native = new RecordingNative();
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
        var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        player.SeekRelative(-10);

        Assert.True(SpinWait.SpinUntil(() => !native.Commands.IsEmpty, TimeSpan.FromSeconds(2)));
        Assert.Contains("seek|-10|relative+exact", native.Commands);
    }

    [Fact]
    public void PlaylistCommandsDispatchCorrectMpvArguments()
    {
        var native = new RecordingNative();
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

    [Fact]
    public void GetPlaybackStats_WhenNoMedia_ReturnsNull()
    {
        var native = new RecordingNative();
        using var player = new LibMpvPlayer(native, action => action());

        var stats = player.GetPlaybackStats();
        Assert.Null(stats);
    }

    [Fact]
    public void GetPlaybackStats_ReturnsPopulatedStatsFromNativeProperties()
    {
        var native = new RecordingNative();
        native.ReadProperties["media-title"] = "Test Title";
        native.ReadProperties["file-format"] = "matroska,webm";
        native.ReadProperties["demuxer"] = "lavf";
        native.ReadProperties["path"] = "https://example.com/video";
        native.ReadProperties["file-size"] = "10485760";
        native.ReadProperties["video-codec"] = "av1";
        native.ReadProperties["video-decoder-name"] = "libdav1d";
        native.ReadProperties["hwdec-current"] = "vaapi";
        native.ReadProperties["video-params/w"] = "1920";
        native.ReadProperties["video-params/h"] = "1080";
        native.ReadProperties["dwidth"] = "1920";
        native.ReadProperties["dheight"] = "1080";
        native.ReadProperties["video-params/aspect"] = "1.777778";
        native.ReadProperties["container-fps"] = "60";
        native.ReadProperties["estimated-vf-fps"] = "59.98";
        native.ReadProperties["video-bitrate"] = "4000000";
        native.ReadProperties["video-params/pixelformat"] = "yuv420p";
        native.ReadProperties["video-params/colormatrix"] = "bt709";
        native.ReadProperties["video-params/colorlevels"] = "limited";
        native.ReadProperties["audio-codec-name"] = "opus";
        native.ReadProperties["audio-params/samplerate"] = "48000";
        native.ReadProperties["audio-params/channels"] = "stereo";
        native.ReadProperties["audio-params/channel-count"] = "2";
        native.ReadProperties["audio-bitrate"] = "160000";
        native.ReadProperties["frame-drop-count"] = "3";
        native.ReadProperties["vo-drop-frame-count"] = "1";
        native.ReadProperties["mistimed-frame-count"] = "0";
        native.ReadProperties["avsync"] = "0.002";
        native.ReadProperties["demuxer-cache-duration"] = "15.5";
        native.ReadProperties["demuxer-cache-state/bytes"] = "5242880";
        native.ReadProperties["percent-pos"] = "42.5";
        native.ReadProperties["mpv-version"] = "mpv 0.38.0";
        native.ReadProperties["ffmpeg-version"] = "7.1";
        native.ReadProperties["track-list/count"] = "1";
        native.ReadProperties["track-list/0/type"] = "video";
        native.ReadProperties["track-list/0/id"] = "1";
        native.ReadProperties["track-list/0/codec"] = "av1";
        native.ReadProperties["track-list/0/demux-w"] = "1920";
        native.ReadProperties["track-list/0/demux-h"] = "1080";
        native.ReadProperties["track-list/0/selected"] = "yes";

        using var player = new LibMpvPlayer(native, action => action());
        var handleFileLoaded = typeof(LibMpvPlayer).GetMethod("HandleFileLoaded",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handleFileLoaded);
        handleFileLoaded.Invoke(player, null);

        var stats = player.GetPlaybackStats();
        Assert.NotNull(stats);
        Assert.Equal("Test Title", stats.Title);
        Assert.Equal("matroska,webm", stats.FileFormat);
        Assert.Equal("lavf", stats.Demuxer);
        Assert.Equal(10485760, stats.FileSize);
        Assert.Equal("av1", stats.VideoCodec);
        Assert.Equal("libdav1d", stats.VideoDecoder);
        Assert.Equal("vaapi", stats.HwDec);
        Assert.Equal(1920, stats.VideoWidth);
        Assert.Equal(1080, stats.VideoHeight);
        Assert.Equal(60, stats.ContainerFps);
        Assert.Equal(4000000, stats.VideoBitrate);
        Assert.Equal("opus", stats.AudioCodec);
        Assert.Equal(48000, stats.AudioSampleRate);
        Assert.Equal("stereo", stats.AudioChannelLayout);
        Assert.Equal(160000, stats.AudioBitrate);
        Assert.Equal(3, stats.DroppedFrames);
        Assert.Equal(15.5, stats.CacheDuration);
        Assert.Equal(5242880, stats.CacheBytes);
        Assert.Equal(42.5, stats.PercentPosition);
        Assert.Equal("mpv 0.38.0", stats.MpvVersion);
        Assert.Equal("7.1", stats.FfmpegVersion);
        Assert.Single(stats.Tracks);
        Assert.Equal("av1", stats.Tracks[0].Codec);
        Assert.True(stats.Tracks[0].IsSelected);
    }

    private static VideoSummary Video(string id)
    {
        return new VideoSummary(id, id, "Channel", TimeSpan.FromMinutes(3), "", false);
    }

    private sealed class RecordingNative : ILibMpvNativeApi
    {
        public ConcurrentBag<string> Commands { get; } = [];
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