using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;

namespace SilverScreen.Browsing.Components;

public sealed record VideoCardServices(
    IThumbnailService Thumbnails,
    INavigationService Navigation,
    IPlaybackService Playback,
    IQueueService Queue,
    INotificationService Notifications);