using System;
using System.Collections.Generic;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockFeedService : IFeedService
{
    private const string DemoPlaybackUrl = "https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/360/Big_Buck_Bunny_360_10s_1MB.mp4";

    private static readonly IReadOnlyList<VideoSummary> HomeVideos =
    [
        new("SsDemoMp4A", "[Demo Playback] Big Buck Bunny sample MP4", "SilverScreen Test Media", TimeSpan.FromSeconds(10), "placeholder://slate", false, DemoPlaybackUrl),
        new("SsGtkBlp02B", "GTK4 Blueprint Patterns That Scale", "GNOME Craft", TimeSpan.FromMinutes(26) + TimeSpan.FromSeconds(5), "placeholder://blue", false),
        new("SsAudioRt03", "PipeWire Routing Without the Panic", "Tux Audio", TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(48), "placeholder://green", false),
        new("SsShortRc04", "One Minute Rice Showcase", "Tiny Terminals", TimeSpan.FromSeconds(58), "placeholder://short", true),
        new("SsCsNative5", "C# Native UI on Linux in 2026", "Managed Penguin", TimeSpan.FromMinutes(33) + TimeSpan.FromSeconds(12), "placeholder://purple", false),
        new("SsBtrfsRc06", "Btrfs Snapshots: Practical Recovery", "Kernel Garden", TimeSpan.FromMinutes(41) + TimeSpan.FromSeconds(27), "placeholder://orange", false),
        new("SsAccess07A", "Accessible App Shells with libadwaita", "Human Interface", TimeSpan.FromMinutes(22) + TimeSpan.FromSeconds(9), "placeholder://teal", false),
        new("SsDotnet08A", ".NET 10 Desktop Tooling Tour", "Runtime Notes", TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(54), "placeholder://red", false),
        new("SsWayland09", "Wayland Windows, Portals, and Sandboxes", "Compositor Club", TimeSpan.FromMinutes(37) + TimeSpan.FromSeconds(40), "placeholder://indigo", false),
    ];

    public FeedPage GetHomeFeed() => new(HomeVideos);
}
