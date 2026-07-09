using System;
using System.Collections.Generic;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockFeedService : IFeedService
{
    private static readonly IReadOnlyList<VideoSummary> HomeVideos =
    [
        new("vid-linux-desktop", "Designing a Calm Linux Desktop", "Northstar Labs", TimeSpan.FromMinutes(18) + TimeSpan.FromSeconds(32), "placeholder://slate", false),
        new("vid-gtk-blueprint", "GTK4 Blueprint Patterns That Scale", "GNOME Craft", TimeSpan.FromMinutes(26) + TimeSpan.FromSeconds(5), "placeholder://blue", false),
        new("vid-audio", "PipeWire Routing Without the Panic", "Tux Audio", TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(48), "placeholder://green", false),
        new("short-rice", "One Minute Rice Showcase", "Tiny Terminals", TimeSpan.FromSeconds(58), "placeholder://short", true),
        new("vid-csharp-native", "C# Native UI on Linux in 2026", "Managed Penguin", TimeSpan.FromMinutes(33) + TimeSpan.FromSeconds(12), "placeholder://purple", false),
        new("vid-filesystems", "Btrfs Snapshots: Practical Recovery", "Kernel Garden", TimeSpan.FromMinutes(41) + TimeSpan.FromSeconds(27), "placeholder://orange", false),
        new("vid-accessibility", "Accessible App Shells with libadwaita", "Human Interface", TimeSpan.FromMinutes(22) + TimeSpan.FromSeconds(9), "placeholder://teal", false),
        new("vid-dotnet", ".NET 10 Desktop Tooling Tour", "Runtime Notes", TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(54), "placeholder://red", false),
        new("vid-windowing", "Wayland Windows, Portals, and Sandboxes", "Compositor Club", TimeSpan.FromMinutes(37) + TimeSpan.FromSeconds(40), "placeholder://indigo", false),
    ];

    public FeedPage GetHomeFeed() => new(HomeVideos);
}
