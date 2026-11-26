using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;

namespace SilverScreen.Browsing.Components;

public sealed record VideoCardServices(
    IThumbnailService Thumbnails,
    INavigationService Navigation,
    IPlaybackService Playback,
    IQueueService Queue,
    INotificationService Notifications,
    ILibraryService? Library = null,
    IDownloadService? Downloads = null,
    IPreferencesService? Preferences = null);