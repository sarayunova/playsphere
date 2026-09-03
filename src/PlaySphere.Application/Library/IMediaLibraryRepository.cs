using PlaySphere.Domain.Library;

namespace PlaySphere.Application.Library;

/// <summary>
/// Persistence boundary for the media library catalog (PICKLE.md §22/§23/§42). UI and other
/// modules depend on this contract, never on the SQLite/EF Core implementation directly
/// (PlaySphere.Infrastructure) — mirrors how playback goes through
/// <c>IMediaPlaybackEngine</c> instead of calling libVLC directly.
/// </summary>
public interface IMediaLibraryRepository
{
    /// <summary>Adds a folder to the library. Throws if the path is already present.</summary>
    Task<LibraryFolder> AddFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the folder registered at <paramref name="path"/>, or registers it (and returns it) if
    /// not already present. Unlike <see cref="AddFolderAsync"/>, does not throw when the folder exists.
    /// </summary>
    Task<LibraryFolder> EnsureFolderAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Removes a folder and cascades removal of its cataloged media items.</summary>
    Task RemoveFolderAsync(int folderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new item or updates the existing one matched by <see cref="LibraryMediaItem.Fingerprint"/>.
    /// Returns the persisted row (with its assigned Id).
    /// </summary>
    Task<LibraryMediaItem> UpsertMediaItemAsync(LibraryMediaItem item, CancellationToken cancellationToken = default);

    Task<LibraryMediaItem?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    Task<LibraryMediaItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken = default);

    Task RemoveMediaItemAsync(int mediaItemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryMediaItem>> GetAllMediaItemsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryMediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    /// <summary>Items with recorded progress that aren't finished, most recently played first.</summary>
    Task<IReadOnlyList<LibraryMediaItem>> GetContinueWatchingAsync(int maxCount = 20, CancellationToken cancellationToken = default);

    /// <summary>All ever-played items, most recently played first.</summary>
    Task<IReadOnlyList<LibraryMediaItem>> GetRecentlyPlayedAsync(int maxCount = 20, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive substring match over title/album/artist.</summary>
    Task<IReadOnlyList<LibraryMediaItem>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(int mediaItemId, bool isFavorite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a playback session: appends a <see cref="PlaybackHistoryEntry"/> and updates the
    /// item's last-played timestamp, last position and watched percentage.
    /// </summary>
    Task RecordPlaybackAsync(int mediaItemId, TimeSpan position, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Sets (or clears, with null) the cached cover-art/thumbnail path for an item.</summary>
    Task SetCoverArtPathAsync(int mediaItemId, string? coverArtPath, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------------------------
    // Bookmarks (PICKLE.md §21)
    // ------------------------------------------------------------------------------------

    /// <summary>Adds a bookmark at a timestamp and returns the persisted row.</summary>
    Task<Bookmark> AddBookmarkAsync(int mediaItemId, long positionMs, string? label = null, string? note = null, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>All bookmarks for an item, ordered by position.</summary>
    Task<IReadOnlyList<Bookmark>> GetBookmarksAsync(int mediaItemId, CancellationToken cancellationToken = default);

    Task RemoveBookmarkAsync(int bookmarkId, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------------------------
    // Playlists (PICKLE.md §25)
    // ------------------------------------------------------------------------------------

    /// <summary>Creates a named playlist and returns the persisted row.</summary>
    Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default);

    Task RenamePlaylistAsync(int playlistId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes a playlist and all of its items.</summary>
    Task DeletePlaylistAsync(int playlistId, CancellationToken cancellationToken = default);

    /// <summary>All playlists, ordered by creation (oldest first).</summary>
    Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Appends a cataloged item to the end of a playlist.</summary>
    Task<PlaylistItem> AddToPlaylistAsync(int playlistId, int mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Appends a set of cataloged items (e.g. from an import) to the end of a playlist, preserving order.</summary>
    Task AddRangeToPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIds, CancellationToken cancellationToken = default);

    /// <summary>Removes a specific playlist item row (not the media item itself).</summary>
    Task RemoveFromPlaylistAsync(int playlistItemId, CancellationToken cancellationToken = default);

    /// <summary>Reorders a playlist to the given media-item id sequence (full replacement of order).</summary>
    Task ReorderPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIdsInOrder, CancellationToken cancellationToken = default);

    /// <summary>All items of a playlist, in position order.</summary>
    Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(int playlistId, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------------------------
    // AI transcripts, chapters and artifacts (PICKLE.md §38/§42, AI-03..08)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the transcript (and its ordered segments) for a media item with the given one.
    /// One transcript per media item is kept (latest wins). Returns the persisted transcript row.
    /// </summary>
    Task<AiTranscript> SaveTranscriptAsync(AiTranscript transcript, IReadOnlyList<AiTranscriptSegment> segments, CancellationToken cancellationToken = default);

    /// <summary>Returns the transcript with its ordered segments for an item, or null if none.</summary>
    Task<AiTranscript?> GetTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the transcript (and segments) for an item.</summary>
    Task DeleteTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the auto-generated chapters for an item with the given ones (latest generation wins).</summary>
    Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default);

    /// <summary>All auto-generated chapters for an item, ordered by start time.</summary>
    Task<IReadOnlyList<AiChapterEntity>> GetChaptersAsync(int mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Upserts/creates an artifact under a key for an item, returning the persisted row.</summary>
    Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default);

    /// <summary>All artifacts for an item, in creation order.</summary>
    Task<IReadOnlyList<AiArtifact>> GetArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default);

    /// <summary>Deletes all artifacts for an item (e.g. when AI output for it is regenerated from scratch).</summary>
    Task DeleteArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default);
}
