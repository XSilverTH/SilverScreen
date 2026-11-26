using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;
using SilverScreen.Player;
using SilverScreen.Shell.Notifications;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Downloads;

public partial class LibraryView : ViewBase<Bin>
{
    private static readonly ILogger Logger = Log.ForContext<LibraryView>();

    private readonly ConditionalWeakTable<ListItem, LibraryItemCardView> _cardsByListItem = new();
    private readonly Dictionary<Guid, DownloadJobRowView> _downloadRowsById = [];
    private readonly IDownloadService _downloads;
    private readonly SignalListItemFactory _factory;
    private readonly ILibraryService _library;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly IThumbnailService _thumbnails;
    private readonly StringList _entryIds;
    private readonly Dictionary<string, LibraryEntry> _entriesById = new(StringComparer.Ordinal);
    private LibraryEntry[] _displayedEntries = [];

    public LibraryView(
        IDownloadService downloads,
        ILibraryService library,
        IThumbnailService thumbnails,
        INavigationService navigation,
        IPlaybackService playback,
        IQueueService queue,
        INotificationService notifications)
    {
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        _entryIds = Lifetime.Own(StringList.New([]));
        var selection = Lifetime.Own(NoSelection.New(_entryIds));
        _factory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(
            () =>
            {
                _factory.OnSetup += OnCardSetup;
                _factory.OnBind += OnCardBind;
                _factory.OnUnbind += OnCardUnbind;
                _factory.OnTeardown += OnCardTeardown;
            },
            () =>
            {
                _factory.OnSetup -= OnCardSetup;
                _factory.OnBind -= OnCardBind;
                _factory.OnUnbind -= OnCardUnbind;
                _factory.OnTeardown -= OnCardTeardown;
            });

        library_grid.Model = selection;
        Lifetime.Track(
            () => library_grid.Factory = _factory,
            () => library_grid.Factory = null);

        Lifetime.Track(
            () => _downloads.JobsChanged += OnDownloadsChanged,
            () => _downloads.JobsChanged -= OnDownloadsChanged);
        Lifetime.Track(
            () => _library.LibraryChanged += OnLibraryChanged,
            () => _library.LibraryChanged -= OnLibraryChanged);

        RenderDownloads();
        RenderLibrary();
    }

    public async Task RefreshAsync()
    {
        await _library.ReloadAsync().ConfigureAwait(false);
        GtkUiDispatch.Run(() => !IsDisposed, () =>
        {
            RenderDownloads();
            RenderLibrary();
        });
    }

    private void OnDownloadsChanged(object? sender, EventArgs e)
    {
        GtkUiDispatch.Run(() => !IsDisposed, RenderDownloads);
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        GtkUiDispatch.Run(() => !IsDisposed, RenderLibrary);
    }

    private void OnSearchChanged(object? sender, EventArgs args)
    {
        RenderLibrary();
    }

    private void OnFilterChanged(object? sender, EventArgs args)
    {
        RenderLibrary();
    }

    private void OnClearFinishedClicked(object? sender, EventArgs args)
    {
        _downloads.ClearFinishedAsync().FireAndForget(Logger);
    }

    private void RenderDownloads()
    {
        var jobs = _downloads.Jobs;
        downloads_section.Visible = jobs.Count > 0;
        if (jobs.Count == 0)
        {
            ClearDownloadRows();
            clear_finished_button.Visible = false;
            return;
        }

        var activeCount = jobs.Count(static j => j.IsActive);
        downloads_heading.SetText(activeCount > 0 ? $"Downloads ({activeCount} active)" : "Downloads");
        clear_finished_button.Visible = jobs.Any(static j => j.IsFinished);

        var keepIds = new HashSet<Guid>(jobs.Select(static j => j.Id));
        var staleIds = _downloadRowsById.Keys.Where(id => !keepIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
        {
            if (_downloadRowsById.Remove(staleId, out var staleRow))
            {
                downloads_list.Remove(staleRow.Widget);
                staleRow.Dispose();
            }
        }

        // Rebuild order cleanly when jobs are inserted/reordered while reusing row widgets.
        foreach (var row in _downloadRowsById.Values)
        {
            if (row.Widget.GetParent() is not null)
                downloads_list.Remove(row.Widget);
        }

        foreach (var job in jobs)
        {
            if (!_downloadRowsById.TryGetValue(job.Id, out var row))
            {
                row = new DownloadJobRowView(_downloads, _thumbnails);
                _downloadRowsById[job.Id] = row;
            }

            row.Bind(job);
            downloads_list.Append(row.Widget);
        }
    }

    private void RenderLibrary()
    {
        var allEntries = _library.Entries;
        var query = search_entry.GetText().Trim();
        var filterIdx = (int)filter_dropdown.GetSelected();

        var filtered = allEntries.Where(entry =>
        {
            if (filterIdx == 1 && entry.MediaMode != DownloadMediaMode.VideoAndAudio)
                return false;
            if (filterIdx == 2 && entry.MediaMode != DownloadMediaMode.AudioOnly)
                return false;
            if (!string.IsNullOrEmpty(query) &&
                !entry.Video.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !entry.Video.ChannelName.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }).ToArray();

        long totalBytes = 0;
        foreach (var e in allEntries)
            totalBytes += Math.Max(0, e.FileSizeBytes);
        var noun = allEntries.Count == 1 ? "item" : "items";
        library_summary_label.SetText(allEntries.Count == 0
            ? "0 items"
            : $"{allEntries.Count} {noun} · {DownloadActionHelper.FormatBytes(totalBytes)}");

        ApplyEntries(filtered);

        if (filtered.Length > 0)
        {
            library_content_stack.VisibleChildName = "grid";
            return;
        }

        if (allEntries.Count == 0)
        {
            library_empty_page.Title = "Your Library is empty";
            library_empty_page.Description = "Download videos to browse and watch them offline";
            library_empty_page.IconName = "folder-download-symbolic";
        }
        else
        {
            library_empty_page.Title = "No matching items";
            library_empty_page.Description = "Try a different search term or filter";
            library_empty_page.IconName = "edit-find-symbolic";
        }

        library_content_stack.VisibleChildName = "empty";
    }

    private void ApplyEntries(LibraryEntry[] nextEntries)
    {
        var changed = _displayedEntries.Length != nextEntries.Length;
        if (!changed)
        {
            for (var i = 0; i < nextEntries.Length; i++)
                if (!Equals(_displayedEntries[i], nextEntries[i]))
                {
                    changed = true;
                    break;
                }
        }

        if (!changed)
            return;

        _displayedEntries = nextEntries;
        _entriesById.Clear();
        var ids = new string[nextEntries.Length];
        for (var i = 0; i < nextEntries.Length; i++)
        {
            var id = nextEntries[i].Video.Id;
            _entriesById[id] = nextEntries[i];
            ids[i] = id;
        }

        _entryIds.Splice(0, _entryIds.GetNItems(), ids);
    }

    private void ClearDownloadRows()
    {
        foreach (var row in _downloadRowsById.Values)
        {
            if (row.Widget.GetParent() is not null)
                downloads_list.Remove(row.Widget);
            row.Dispose();
        }

        _downloadRowsById.Clear();
    }

    private void OnCardSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem) return;
        var card = new LibraryItemCardView(_library, _thumbnails, _navigation, _playback, _queue, _notifications);
        listItem.Child = card.Widget;
        _cardsByListItem.Add(listItem, card);
    }

    private void OnCardBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Item: StringObject { String: { } id } } listItem ||
            !_cardsByListItem.TryGetValue(listItem, out var card) ||
            !_entriesById.TryGetValue(id, out var entry))
            return;

        card.Bind(entry);
    }

    private void OnCardUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem listItem && _cardsByListItem.TryGetValue(listItem, out var card))
            card.Unbind();
    }

    private void OnCardTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem || !_cardsByListItem.TryGetValue(listItem, out var card))
            return;

        _cardsByListItem.Remove(listItem);
        listItem.Child = null;
        card.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearDownloadRows();
            foreach (var pair in _cardsByListItem)
            {
                pair.Key.Child = null;
                pair.Value.Dispose();
            }

            _cardsByListItem.Clear();
        }

        base.Dispose(disposing);
    }
}
