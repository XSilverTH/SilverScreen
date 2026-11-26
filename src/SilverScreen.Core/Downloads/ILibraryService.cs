namespace SilverScreen.Core.Downloads;

/// <summary>
///     Manages the offline Library index stored inside the configured Library directory.
/// </summary>
public interface ILibraryService
{
    string LibraryDirectory { get; }

    IReadOnlyList<LibraryEntry> Entries { get; }

    event EventHandler? LibraryChanged;

    LibraryEntry? TryGetEntry(string videoId);

    LibraryEntry? TryGetPlayableEntry(string videoId);

    bool ContainsPlayable(string videoId);

    string? TryGetThumbnailPath(string videoId);

    Task UpsertAsync(LibraryEntry entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(string videoId, bool deleteFileFromDisk = false, CancellationToken cancellationToken = default);

    Task ReloadAsync(CancellationToken cancellationToken = default);
}