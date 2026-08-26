using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace SilverScreen.Core.Player;

public static class PlaybackStatsFormatter
{
    public static string FormatBitrate(double? bitsPerSec)
    {
        if (bitsPerSec is null or <= 0 || double.IsNaN(bitsPerSec.Value) || double.IsInfinity(bitsPerSec.Value))
            return "—";

        var bps = bitsPerSec.Value;
        return bps switch
        {
            >= 1_000_000_000 => $"{bps / 1_000_000_000:0.00} Gbps",
            >= 1_000_000 => $"{bps / 1_000_000:0.00} Mbps",
            >= 1_000 => $"{bps / 1_000:#,##0} kbps",
            _ => $"{(int)bps} bps"
        };
    }

    public static string FormatBytes(long? bytes)
    {
        if (bytes is null or <= 0) return "—";

        var b = bytes.Value;
        return b switch
        {
            >= 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024 * 1024):0.00} GB",
            >= 1024L * 1024 => $"{b / (1024.0 * 1024):0.0} MB",
            >= 1024 => $"{b / 1024.0:0.0} KB",
            _ => $"{b} B"
        };
    }

    public static string FormatFps(double? containerFps, double? estimatedFps)
    {
        var hasContainer = containerFps is > 0 and not double.NaN and not double.PositiveInfinity;
        var hasEstimated = estimatedFps is > 0 and not double.NaN and not double.PositiveInfinity;

        switch (hasContainer)
        {
            case true when hasEstimated && containerFps.HasValue && estimatedFps.HasValue:
                return $"{containerFps.Value:0.00} fps (container) / {estimatedFps.Value:0.00} fps (estimated)";
            case true when containerFps.HasValue:
                return $"{containerFps.Value:0.00} fps";
        }

        if (hasEstimated && estimatedFps.HasValue)
            return $"{estimatedFps.Value:0.00} fps (estimated)";

        return "—";
    }

    public static string FormatResolution(int? w, int? h, int? dw, int? dh, double? aspect)
    {
        if (w is null or <= 0 || h is null or <= 0) return "—";

        var aspectStr = aspect is > 0 ? $" (Aspect: {aspect.Value:0.00})" : "";
        if (dw is > 0 && dh is > 0 && (dw != w || dh != h))
            return $"{w}×{h}{aspectStr} → {dw}×{dh}";

        return $"{w}×{h}{aspectStr}";
    }

    public static string FormatAvSync(double? avsyncSeconds)
    {
        if (avsyncSeconds is null or double.NaN or double.PositiveInfinity or double.NegativeInfinity)
            return "—";

        var ms = avsyncSeconds.Value * 1000.0;
        var sign = ms >= 0 ? "+" : "";
        return $"{sign}{ms:0.00} ms";
    }

    public static string FormatDroppedFrames(long? total, long? vo, long? mistimed)
    {
        var t = total ?? 0;
        var v = vo ?? 0;
        var m = mistimed ?? 0;
        return $"{t} (VO: {v}, Mistimed: {m})";
    }

    public static string FormatCache(double? seconds, long? bytes)
    {
        var hasSec = seconds is >= 0 and not double.NaN and not double.PositiveInfinity;
        var hasBytes = bytes is > 0;

        return hasSec switch
        {
            true when hasBytes && seconds.HasValue => $"{seconds.Value:0.0} s ({FormatBytes(bytes)})",
            true when seconds.HasValue => $"{seconds.Value:0.0} s",
            _ => hasBytes ? FormatBytes(bytes) : "—"
        };
    }

    public static string FormatTime(TimeSpan position, TimeSpan duration, double? percent)
    {
        var posStr = FormatDuration(position);
        var durStr = FormatDuration(duration);
        return percent is > 0 ? $"{posStr} / {durStr} ({percent.Value:0.0}%)" : $"{posStr} / {durStr}";
    }

    public static string FormatOverviewPageMarkup(PlaybackStats stats, string accentColor = "#78aeed")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>[1/4] Overview</b></span>\n");

        // 1. File and stream
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>File</b></span>");
        if (!string.IsNullOrWhiteSpace(stats.Title))
            sb.AppendLine($"  <span foreground=\"#9a9996\">Title:</span> <b>{Escape(stats.Title)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Format:</span> <b>{Escape(stats.FileFormat ?? "—")}</b> (Demuxer: <b>{Escape(stats.Demuxer ?? "—")}</b>)");
        if (stats.FileSize is > 0 || !string.IsNullOrWhiteSpace(stats.ProtocolOrUrl))
        {
            var sizeStr = stats.FileSize is > 0 ? FormatBytes(stats.FileSize) : null;
            var protoStr = !string.IsNullOrWhiteSpace(stats.ProtocolOrUrl)
                ? stats.ProtocolOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? "https/http stream"
                    : "file/local"
                : null;
            if (sizeStr != null && protoStr != null)
                sb.AppendLine(
                    $"  <span foreground=\"#9a9996\">Size:</span> <b>{sizeStr}</b> • <span foreground=\"#9a9996\">Source:</span> <b>{protoStr}</b>");
            else if (sizeStr != null)
                sb.AppendLine($"  <span foreground=\"#9a9996\">Size:</span> <b>{sizeStr}</b>");
            else if (protoStr != null)
                sb.AppendLine($"  <span foreground=\"#9a9996\">Source:</span> <b>{protoStr}</b>");
        }

        sb.AppendLine();

        // 2. Video stream
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Video</b></span>");
        var vCodec = stats.VideoCodec ?? "—";
        var vDec = stats.VideoDecoder != null && stats.VideoDecoder != stats.VideoCodec
            ? $" [Decoder: <b>{Escape(stats.VideoDecoder)}</b>]"
            : "";
        sb.AppendLine($"  <span foreground=\"#9a9996\">Codec:</span> <b>{Escape(vCodec)}</b>{vDec}");
        if (!string.IsNullOrWhiteSpace(stats.HwDec))
        {
            var isHw = !string.Equals(stats.HwDec, "no", StringComparison.OrdinalIgnoreCase);
            var hwText = isHw ? $"{Escape(stats.HwDec)} (hw)" : "software (no)";
            sb.AppendLine($"  <span foreground=\"#9a9996\">Hardware Decoding:</span> <b>{hwText}</b>");
        }

        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Resolution:</span> <b>{FormatResolution(stats.VideoWidth, stats.VideoHeight, stats.DisplayWidth, stats.DisplayHeight, stats.AspectRatio)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Frame Rate:</span> <b>{FormatFps(stats.ContainerFps, stats.EstimatedFps)}</b>");
        sb.AppendLine($"  <span foreground=\"#9a9996\">Bitrate:</span> <b>{FormatBitrate(stats.VideoBitrate)}</b>");
        if (!string.IsNullOrWhiteSpace(stats.PixelFormat))
            sb.AppendLine($"  <span foreground=\"#9a9996\">Pixel Format:</span> <b>{Escape(stats.PixelFormat)}</b>");
        if (!string.IsNullOrWhiteSpace(stats.ColorMatrix) || !string.IsNullOrWhiteSpace(stats.ColorLevels) ||
            !string.IsNullOrWhiteSpace(stats.Primaries))
        {
            var matrix = stats.ColorMatrix ?? "—";
            var levels = stats.ColorLevels != null ? $" ({stats.ColorLevels})" : "";
            var prim = stats.Primaries != null ? $" • Primaries: <b>{Escape(stats.Primaries)}</b>" : "";
            var gam = stats.Gamma != null ? $" • Gamma: <b>{Escape(stats.Gamma)}</b>" : "";
            sb.AppendLine(
                $"  <span foreground=\"#9a9996\">Color Space:</span> <b>{Escape(matrix)}{levels}</b>{prim}{gam}");
        }

        sb.AppendLine();

        // 3. Audio stream
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Audio</b></span>");
        var aCodec = stats.AudioCodec ?? "—";
        var aSr = stats.AudioSampleRate is > 0 ? $"{stats.AudioSampleRate.Value:#,##0} Hz" : null;
        var aCh = stats.AudioChannelLayout ?? (stats.AudioChannels is > 0 ? $"{stats.AudioChannels.Value} ch" : null);
        var aFmt = stats.AudioFormat;
        var aDetailsList = new List<string>();
        if (aSr != null) aDetailsList.Add(aSr);
        if (aCh != null) aDetailsList.Add(aCh);
        if (aFmt != null) aDetailsList.Add(aFmt);
        var aDetails = aDetailsList.Count > 0 ? $" ({string.Join(", ", aDetailsList)})" : "";
        sb.AppendLine($"  <span foreground=\"#9a9996\">Codec:</span> <b>{Escape(aCodec)}</b>{aDetails}");
        sb.AppendLine($"  <span foreground=\"#9a9996\">Bitrate:</span> <b>{FormatBitrate(stats.AudioBitrate)}</b>");
        sb.AppendLine();

        // 4. Performance
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Performance</b></span>");
        sb.AppendLine($"  <span foreground=\"#9a9996\">A/V Sync:</span> <b>{FormatAvSync(stats.AvSyncDifference)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Dropped Frames:</span> <b>{FormatDroppedFrames(stats.DroppedFrames, stats.VoDroppedFrames, stats.MistimedFrames)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Demuxer Cache:</span> <b>{FormatCache(stats.CacheDuration, stats.CacheBytes)}</b>");
        sb.AppendLine();

        // 5. Playback
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Playback</b></span>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Position:</span> <b>{FormatTime(stats.Position, stats.Duration, stats.PercentPosition)}</b>");
        var mutedStr = stats.IsMuted ? "<span foreground=\"#e01b24\"><b>Yes</b></span>" : "No";
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Speed:</span> <b>{stats.Speed:0.00}×</b> • <span foreground=\"#9a9996\">Volume:</span> <b>{(int)stats.Volume}%</b> (Muted: {mutedStr})");
        if (!string.IsNullOrWhiteSpace(stats.SubtitleTrack))
            sb.AppendLine($"  <span foreground=\"#9a9996\">Subtitles:</span> <b>{Escape(stats.SubtitleTrack)}</b>");

        return sb.ToString().TrimEnd();
    }

    public static string FormatPerformancePageMarkup(PlaybackStats stats, string accentColor = "#78aeed")
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"<span weight=\"bold\" foreground=\"{accentColor}\"><b>[2/4] Performance and timings</b></span>\n");

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Rendering</b></span>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Estimated Output FPS:</span> <b>{(stats.EstimatedFps is > 0 ? $"{stats.EstimatedFps.Value:0.00} fps" : "—")}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Container Frame Rate:</span> <b>{(stats.ContainerFps is > 0 ? $"{stats.ContainerFps.Value:0.00} fps" : "—")}</b>");
        if (stats.VsyncRatio is not null)
            sb.AppendLine($"  <span foreground=\"#9a9996\">Vsync Ratio:</span> <b>{stats.VsyncRatio.Value:0.000}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Decoder Dropped Frames:</span> <b>{stats.DroppedFrames ?? 0}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">VO Output Dropped Frames:</span> <b>{stats.VoDroppedFrames ?? 0}</b>");
        sb.AppendLine($"  <span foreground=\"#9a9996\">Mistimed Frames:</span> <b>{stats.MistimedFrames ?? 0}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">A/V Sync Difference:</span> <b>{FormatAvSync(stats.AvSyncDifference)}</b>");
        sb.AppendLine();

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Bitrate</b></span>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Current Video Bitrate:</span> <b>{FormatBitrate(stats.VideoBitrate)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Current Audio Bitrate:</span> <b>{FormatBitrate(stats.AudioBitrate)}</b>");
        var totalBitrate = (stats.VideoBitrate ?? 0) + (stats.AudioBitrate ?? 0);
        if (totalBitrate > 0)
            sb.AppendLine(
                $"  <span foreground=\"#9a9996\">Total Stream Bitrate:</span> <b>{FormatBitrate(totalBitrate)}</b>");
        sb.AppendLine();

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Cache</b></span>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Buffered Duration:</span> <b>{(stats.CacheDuration is > 0 ? $"{stats.CacheDuration.Value:0.0} s" : "—")}</b>");
        sb.AppendLine($"  <span foreground=\"#9a9996\">Buffered Data:</span> <b>{FormatBytes(stats.CacheBytes)}</b>");
        sb.AppendLine($"  <span foreground=\"#9a9996\">Demuxer Engine:</span> <b>{Escape(stats.Demuxer ?? "—")}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Container Format:</span> <b>{Escape(stats.FileFormat ?? "—")}</b>");

        return sb.ToString().TrimEnd();
    }

    public static string FormatTracksPageMarkup(PlaybackStats stats, string accentColor = "#78aeed")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>[3/4] Tracks</b></span>\n");

        var videoTracks = stats.Tracks.Where(t => string.Equals(t.Type, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var audioTracks = stats.Tracks.Where(t => string.Equals(t.Type, "audio", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var subTracks = stats.Tracks.Where(t => string.Equals(t.Type, "sub", StringComparison.OrdinalIgnoreCase))
            .ToList();
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Video</b></span>");
        if (videoTracks.Count == 0)
        {
            var codec = stats.VideoCodec ?? "—";
            var res = stats is { VideoWidth: > 0, VideoHeight: > 0 }
                ? $"{stats.VideoWidth}×{stats.VideoHeight}"
                : null;
            var fps = stats.ContainerFps is > 0 ? $"@{stats.ContainerFps.Value:0.00} fps" : null;
            var details = string.Join(" ", new[] { res, fps }.Where(s => s != null));
            sb.AppendLine(
                $"  <b>[#1]</b> <b>{Escape(codec)}</b> {details} <span foreground=\"#33d17a\"><b>[Active]</b></span>");
        }
        else
        {
            foreach (var track in videoTracks)
            {
                var tag = track.IsSelected ? " <span foreground=\"#33d17a\"><b>[Selected]</b></span>" : "";
                var def = track.IsDefault ? " [Default]" : "";
                var dim = track is { Width: > 0, Height: > 0 } ? $" ({track.Width}×{track.Height})" : "";
                var fps = track.Fps is > 0 ? $" @ {track.Fps.Value:0.00} fps" : "";
                var br = track.Bitrate is > 0 ? $" • {FormatBitrate(track.Bitrate)}" : "";
                sb.AppendLine(
                    $"  <b>[#{track.Id}]</b> <b>{Escape(track.Codec ?? "video")}</b>{dim}{fps}{br}{def}{tag}");
            }
        }

        sb.AppendLine();

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Audio</b></span>");
        if (audioTracks.Count == 0)
        {
            var codec = stats.AudioCodec ?? "—";
            var sr = stats.AudioSampleRate is > 0 ? $"{stats.AudioSampleRate.Value} Hz" : null;
            var ch = stats.AudioChannelLayout ??
                     (stats.AudioChannels is > 0 ? $"{stats.AudioChannels.Value} ch" : null);
            var details = string.Join(", ", new[] { sr, ch }.Where(s => s != null));
            var detailsStr = details.Length > 0 ? $" ({details})" : "";
            sb.AppendLine(
                $"  <b>[#1]</b> <b>{Escape(codec)}</b>{detailsStr} <span foreground=\"#33d17a\"><b>[Active]</b></span>");
        }
        else
        {
            foreach (var track in audioTracks)
            {
                var tag = track.IsSelected ? " <span foreground=\"#33d17a\"><b>[Selected]</b></span>" : "";
                var def = track.IsDefault ? " [Default]" : "";
                var title = !string.IsNullOrWhiteSpace(track.Title) ? $" - {Escape(track.Title)}" :
                    !string.IsNullOrWhiteSpace(track.Language) ? $" - {Escape(track.Language)}" : "";
                var sr = track.SampleRate is > 0 ? $" {track.SampleRate.Value} Hz" : "";
                var ch = !string.IsNullOrWhiteSpace(track.Channels) ? $" {Escape(track.Channels)}" : "";
                var br = track.Bitrate is > 0 ? $" • {FormatBitrate(track.Bitrate)}" : "";
                sb.AppendLine(
                    $"  <b>[#{track.Id}]</b> <b>{Escape(track.Codec ?? "audio")}</b>{sr}{ch}{br}{title}{def}{tag}");
            }
        }

        sb.AppendLine();

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Subtitle</b></span>");
        if (subTracks.Count == 0)
            sb.AppendLine("  <span foreground=\"#9a9996\">No subtitle tracks found</span>");
        else
            foreach (var track in subTracks)
            {
                var tag = track.IsSelected ? " <span foreground=\"#33d17a\"><b>[Selected]</b></span>" : "";
                var def = track.IsDefault ? " [Default]" : "";
                var title = !string.IsNullOrWhiteSpace(track.Title) ? Escape(track.Title) :
                    !string.IsNullOrWhiteSpace(track.Language) ? Escape(track.Language) : "Subtitle";
                var codec = !string.IsNullOrWhiteSpace(track.Codec) ? $" ({Escape(track.Codec)})" : "";
                sb.AppendLine($"  <b>[#{track.Id}]</b> {title}{codec}{def}{tag}");
            }

        return sb.ToString().TrimEnd();
    }

    public static string FormatEnginePageMarkup(PlaybackStats stats, string accentColor = "#78aeed")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>[4/4] System</b></span>\n");

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Media</b></span>");
        sb.AppendLine("  <span foreground=\"#9a9996\">Player Engine:</span> <b>libmpv</b>");
        if (!string.IsNullOrWhiteSpace(stats.MpvVersion))
            sb.AppendLine($"  <span foreground=\"#9a9996\">mpv Version:</span> <b>{Escape(stats.MpvVersion)}</b>");
        if (!string.IsNullOrWhiteSpace(stats.FfmpegVersion))
            sb.AppendLine(
                $"  <span foreground=\"#9a9996\">FFmpeg / libavcodec:</span> <b>{Escape(stats.FfmpegVersion)}</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Video Output:</span> <b>{Escape(stats.VoBackend ?? "libmpv (OpenGL / libepoxy)")}</b>");
        if (!string.IsNullOrWhiteSpace(stats.HwDec))
            sb.AppendLine($"  <span foreground=\"#9a9996\">Hardware Decoder:</span> <b>{Escape(stats.HwDec)}</b>");
        sb.AppendLine();

        sb.AppendLine($"<span weight=\"bold\" foreground=\"{accentColor}\"><b>Application</b></span>");
        sb.AppendLine("  <span foreground=\"#9a9996\">App:</span> <b>SilverScreen</b>");
        sb.AppendLine("  <span foreground=\"#9a9996\">Framework:</span> <b>GTK 4 / Libadwaita / .NET 10</b>");
        sb.AppendLine(
            $"  <span foreground=\"#9a9996\">Platform:</span> <b>{Escape(Environment.OSVersion.VersionString)} ({RuntimeInformation.ProcessArchitecture})</b>");

        return sb.ToString().TrimEnd();
    }

    public static string FormatFullSummary(PlaybackStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Playback statistics ===");
        sb.AppendLine();

        sb.AppendLine("[File]");
        if (!string.IsNullOrWhiteSpace(stats.Title))
            sb.AppendLine($"Title: {stats.Title}");
        sb.AppendLine($"Format: {stats.FileFormat ?? "—"} (Demuxer: {stats.Demuxer ?? "—"})");
        if (stats.FileSize is > 0)
            sb.AppendLine($"Size: {FormatBytes(stats.FileSize)}");
        if (!string.IsNullOrWhiteSpace(stats.ProtocolOrUrl))
            sb.AppendLine($"Source: {stats.ProtocolOrUrl}");
        sb.AppendLine();

        sb.AppendLine("[Video]");
        sb.AppendLine(
            $"Codec: {stats.VideoCodec ?? "—"}{(stats.VideoDecoder != null && stats.VideoDecoder != stats.VideoCodec ? $" (Decoder: {stats.VideoDecoder})" : "")}");
        if (!string.IsNullOrWhiteSpace(stats.HwDec))
            sb.AppendLine($"Hardware Decoding: {stats.HwDec}");
        sb.AppendLine(
            $"Resolution: {FormatResolution(stats.VideoWidth, stats.VideoHeight, stats.DisplayWidth, stats.DisplayHeight, stats.AspectRatio)}");
        sb.AppendLine($"Frame Rate: {FormatFps(stats.ContainerFps, stats.EstimatedFps)}");
        sb.AppendLine($"Bitrate: {FormatBitrate(stats.VideoBitrate)}");
        if (!string.IsNullOrWhiteSpace(stats.PixelFormat))
            sb.AppendLine($"Pixel Format: {stats.PixelFormat}");
        if (!string.IsNullOrWhiteSpace(stats.ColorMatrix) || !string.IsNullOrWhiteSpace(stats.ColorLevels) ||
            !string.IsNullOrWhiteSpace(stats.Primaries))
            sb.AppendLine(
                $"Color Space: Matrix: {stats.ColorMatrix ?? "—"}{(stats.ColorLevels != null ? $" ({stats.ColorLevels})" : "")}, Primaries: {stats.Primaries ?? "—"}, Gamma: {stats.Gamma ?? "—"}");
        sb.AppendLine();

        sb.AppendLine("[Audio]");
        sb.AppendLine(
            $"Codec: {stats.AudioCodec ?? "—"}{(stats.AudioDecoder != null && stats.AudioDecoder != stats.AudioCodec ? $" (Decoder: {stats.AudioDecoder})" : "")}");
        if (stats.AudioSampleRate is > 0)
            sb.AppendLine($"Sample Rate: {stats.AudioSampleRate.Value:#,##0} Hz");
        if (stats.AudioChannels is > 0 || !string.IsNullOrWhiteSpace(stats.AudioChannelLayout))
            sb.AppendLine($"Channels: {stats.AudioChannelLayout ?? $"{stats.AudioChannels} ch"}");
        if (!string.IsNullOrWhiteSpace(stats.AudioFormat))
            sb.AppendLine($"Format: {stats.AudioFormat}");
        sb.AppendLine($"Bitrate: {FormatBitrate(stats.AudioBitrate)}");
        sb.AppendLine();

        sb.AppendLine("[Performance]");
        sb.AppendLine($"A/V Sync: {FormatAvSync(stats.AvSyncDifference)}");
        sb.AppendLine(
            $"Dropped Frames: {FormatDroppedFrames(stats.DroppedFrames, stats.VoDroppedFrames, stats.MistimedFrames)}");
        if (stats.VsyncRatio is not null)
            sb.AppendLine($"Vsync Ratio: {stats.VsyncRatio.Value:0.000}");
        sb.AppendLine($"Demuxer Cache: {FormatCache(stats.CacheDuration, stats.CacheBytes)}");
        sb.AppendLine();

        sb.AppendLine("[Playback]");
        sb.AppendLine($"Position: {FormatTime(stats.Position, stats.Duration, stats.PercentPosition)}");
        sb.AppendLine($"Speed: {stats.Speed:0.00}x");
        sb.AppendLine($"Volume: {(int)stats.Volume}% (Muted: {(stats.IsMuted ? "Yes" : "No")})");
        if (!string.IsNullOrWhiteSpace(stats.SubtitleTrack))
            sb.AppendLine($"Subtitles: {stats.SubtitleTrack}");
        sb.AppendLine();

        if (stats.Tracks.Count > 0)
        {
            sb.AppendLine("[Tracks]");
            foreach (var track in stats.Tracks)
            {
                var sel = track.IsSelected ? " [Selected]" : "";
                var def = track.IsDefault ? " [Default]" : "";
                var title = !string.IsNullOrWhiteSpace(track.Title) ? $" - {track.Title}" :
                    !string.IsNullOrWhiteSpace(track.Language) ? $" - {track.Language}" : "";
                sb.AppendLine($"- {track.Type} #{track.Id}: {track.Codec ?? "unknown"}{title}{def}{sel}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("[Engine]");
        sb.AppendLine("Engine: libmpv");
        if (!string.IsNullOrWhiteSpace(stats.MpvVersion))
            sb.AppendLine($"mpv Version: {stats.MpvVersion}");
        if (!string.IsNullOrWhiteSpace(stats.FfmpegVersion))
            sb.AppendLine($"FFmpeg Version: {stats.FfmpegVersion}");
        sb.AppendLine($"Video Output: {stats.VoBackend ?? "libmpv (OpenGL)"}");

        return sb.ToString().TrimEnd();
    }

    private static string FormatDuration(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    private static string Escape(string text)
    {
        return SecurityElement.Escape(text);
    }
}