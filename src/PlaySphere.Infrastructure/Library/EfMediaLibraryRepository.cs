using Microsoft.EntityFrameworkCore;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;

namespace PlaySphere.Infrastructure.Library;

/// <summary>
/// EF Core / SQLite implementation of <see cref="IMediaLibraryRepository"/>. Creates a short-lived
/// <see cref="PlaySphereDbContext"/> per operation via <see cref="IDbContextFactory{TContext}"/> —
/// safe to call concurrently (e.g. from a background scanner and the UI at once), since a single
/// long-lived DbContext instance is not thread-safe.
/// </summary>
public sealed class EfMediaLibraryRepository(IDbContextFactory<PlaySphereDbContext> contextFactory) : IMediaLibraryRepository
{
    /// <summary>An item is "finished" (excluded from Continue Watching) at or above this watched percentage.</summary>
    private const double FinishedWatchedPercentageThreshold = 95.0;

    public async Task<LibraryFolder> AddFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var canonicalPath = Path.GetFullPath(path);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.LibraryFolders.FirstOrDefaultAsync(f => f.Path == canonicalPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Folder is already in the library: {canonicalPath}");
        }

        var folder = new LibraryFolder { Path = canonicalPath, AddedAt = DateTime.UtcNow };
        db.LibraryFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return folder;
    }

    public async Task<LibraryFolder> EnsureFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var canonicalPath = Path.GetFullPath(path);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.LibraryFolders.FirstOrDefaultAsync(f => f.Path == canonicalPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var folder = new LibraryFolder { Path = canonicalPath, AddedAt = DateTime.UtcNow };
        db.LibraryFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return folder;
    }

    public async Task RemoveFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Explicit cascade: SQLite does not enforce FK-cascade delete unless PRAGMA foreign_keys
        // is enabled on the connection, so history/media rows are removed here rather than relying on it.
        var mediaItemIds = await db.MediaItems
            .Where(m => m.LibraryFolderId == folderId)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (mediaItemIds.Count > 0)
        {
            await db.PlaybackHistory.Where(h => mediaItemIds.Contains(h.MediaItemId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.MediaItems.Where(m => m.LibraryFolderId == folderId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await db.LibraryFolders.Where(f => f.Id == folderId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.LibraryFolders.OrderBy(f => f.Path).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryMediaItem> UpsertMediaItemAsync(LibraryMediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.MediaItems.FirstOrDefaultAsync(m => m.Fingerprint == item.Fingerprint, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.MediaItems.Add(item);
        }
        else
        {
            existing.FilePath = item.FilePath;
            existing.Title = item.Title;
            existing.Duration = item.Duration;
            existing.Resolution = item.Resolution;
            existing.Codec = item.Codec;
            existing.FrameRate = item.FrameRate;
            existing.BitrateKbps = item.BitrateKbps;
            existing.AudioTrackCount = item.AudioTrackCount;
            existing.SubtitleTrackCount = item.SubtitleTrackCount;
            existing.Album = item.Album;
            existing.Artist = item.Artist;
            existing.CoverArtPath = item.CoverArtPath;
            existing.LibraryFolderId = item.LibraryFolderId;
            item = existing;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    public async Task<LibraryMediaItem?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Fingerprint == fingerprint, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryMediaItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.FilePath == filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveMediaItemAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.PlaybackHistory.Where(h => h.MediaItemId == mediaItemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.MediaItems.Where(m => m.Id == mediaItemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryMediaItem>> GetAllMediaItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking().OrderBy(m => m.Title ?? m.FilePath).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryMediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking()
            .Where(m => m.IsFavorite)
            .OrderBy(m => m.Title ?? m.FilePath)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryMediaItem>> GetContinueWatchingAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking()
            .Where(m => m.LastPlayedAt != null && m.WatchedPercentage > 0 && m.WatchedPercentage < FinishedWatchedPercentageThreshold)
            .OrderByDescending(m => m.LastPlayedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryMediaItem>> GetRecentlyPlayedAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.MediaItems.AsNoTracking()
            .Where(m => m.LastPlayedAt != null)
            .OrderByDescending(m => m.LastPlayedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryMediaItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // EF.Functions.Like with SQLite's default collation is case-insensitive for ASCII.
        var pattern = $"%{query}%";
        return await db.MediaItems.AsNoTracking()
            .Where(m =>
                EF.Functions.Like(m.Title ?? string.Empty, pattern) ||
                EF.Functions.Like(m.Album ?? string.Empty, pattern) ||
                EF.Functions.Like(m.Artist ?? string.Empty, pattern))
            .OrderBy(m => m.Title ?? m.FilePath)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFavoriteAsync(int mediaItemId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.MediaItems.Where(m => m.Id == mediaItemId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsFavorite, isFavorite), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RecordPlaybackAsync(int mediaItemId, TimeSpan position, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var playedAt = DateTime.UtcNow;
        db.PlaybackHistory.Add(new PlaybackHistoryEntry
        {
            MediaItemId = mediaItemId,
            PlayedAt = playedAt,
            Position = position,
            Duration = duration,
        });

        var watchedPercentage = duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 100.0, 0.0, 100.0)
            : 0.0;

        await db.MediaItems.Where(m => m.Id == mediaItemId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.LastPlayedAt, playedAt)
                .SetProperty(m => m.LastPosition, position)
                .SetProperty(m => m.WatchedPercentage, watchedPercentage), cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCoverArtPathAsync(int mediaItemId, string? coverArtPath, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.MediaItems.Where(m => m.Id == mediaItemId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.CoverArtPath, coverArtPath), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Bookmark> AddBookmarkAsync(int mediaItemId, long positionMs, string? label = null, string? note = null, string? category = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var bookmark = new Bookmark
        {
            MediaItemId = mediaItemId,
            PositionMs = Math.Max(0, positionMs),
            Label = label,
            Note = note,
            Category = category,
            CreatedAt = DateTime.UtcNow,
        };
        db.Bookmarks.Add(bookmark);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return bookmark;
    }

    public async Task<IReadOnlyList<Bookmark>> GetBookmarksAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Bookmarks.AsNoTracking()
            .Where(b => b.MediaItemId == mediaItemId)
            .OrderBy(b => b.PositionMs)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveBookmarkAsync(int bookmarkId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Bookmarks.Where(b => b.Id == bookmarkId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var playlist = new Playlist { Name = name.Trim(), CreatedAt = DateTime.UtcNow };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return playlist;
    }

    public async Task RenamePlaylistAsync(int playlistId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Playlists.Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Name, name.Trim()), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeletePlaylistAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.PlaylistItems.Where(i => i.PlaylistId == playlistId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Playlists.Where(p => p.Id == playlistId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Playlists.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlaylistItem> AddToPlaylistAsync(int playlistId, int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var maxPosition = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .Select(i => (int?)i.Position)
            .MaxAsync(cancellationToken).ConfigureAwait(false) ?? -1;

        var item = new PlaylistItem
        {
            PlaylistId = playlistId,
            MediaItemId = mediaItemId,
            Position = maxPosition + 1,
            AddedAt = DateTime.UtcNow,
        };
        db.PlaylistItems.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    public async Task AddRangeToPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        if (mediaItemIds.Count == 0)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var maxPosition = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .Select(i => (int?)i.Position)
            .MaxAsync(cancellationToken).ConfigureAwait(false) ?? -1;

        var now = DateTime.UtcNow;
        var position = maxPosition + 1;
        foreach (var mediaItemId in mediaItemIds)
        {
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlistId,
                MediaItemId = mediaItemId,
                Position = position++,
                AddedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveFromPlaylistAsync(int playlistItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.PlaylistItems.Where(i => i.Id == playlistItemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIdsInOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIdsInOrder);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byMediaId = rows.ToDictionary(r => r.MediaItemId);

        // If the new order omits an existing row, that row is dropped (full replacement of order).
        var removed = rows.Where(r => !mediaItemIdsInOrder.Contains(r.MediaItemId)).ToList();
        if (removed.Count > 0)
        {
            db.PlaylistItems.RemoveRange(removed);
        }

        for (var position = 0; position < mediaItemIdsInOrder.Count; position++)
        {
            if (byMediaId.TryGetValue(mediaItemIdsInOrder[position], out var row))
            {
                row.Position = position;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.PlaylistItems.AsNoTracking()
            .Where(i => i.PlaylistId == playlistId)
            .OrderBy(i => i.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiTranscript> SaveTranscriptAsync(AiTranscript transcript, IReadOnlyList<AiTranscriptSegment> segments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(segments);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // One transcript per media item (latest wins): remove any existing transcript + segments first.
        var existingId = await db.AiTranscripts
            .Where(t => t.MediaItemId == transcript.MediaItemId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existingId is not null)
        {
            await db.AiTranscriptSegments.Where(s => s.TranscriptId == existingId.Value).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.AiTranscripts.Where(t => t.Id == existingId.Value).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (transcript.CreatedAt == default)
        {
            transcript.CreatedAt = DateTime.UtcNow;
        }

        db.AiTranscripts.Add(transcript);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < segments.Count; i++)
        {
            db.AiTranscriptSegments.Add(new AiTranscriptSegment
            {
                TranscriptId = transcript.Id,
                Ordinal = i,
                StartSeconds = segments[i].StartSeconds,
                EndSeconds = segments[i].EndSeconds,
                Text = segments[i].Text,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return transcript;
    }

    public async Task<AiTranscript?> GetTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var transcript = await db.AiTranscripts.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MediaItemId == mediaItemId, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
        {
            return null;
        }

        transcript.Segments = await db.AiTranscriptSegments.AsNoTracking()
            .Where(s => s.TranscriptId == transcript.Id)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return transcript;
    }

    public async Task DeleteTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var id = await db.AiTranscripts
            .Where(t => t.MediaItemId == mediaItemId)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (id is not null)
        {
            await db.AiTranscriptSegments.Where(s => s.TranscriptId == id.Value).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.AiTranscripts.Where(t => t.Id == id.Value).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Replace the whole chapter set (latest generation wins) and reinsert at start order.
        await db.AiChapters.Where(c => c.MediaItemId == mediaItemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var chapter in chapters)
        {
            db.AiChapters.Add(new AiChapterEntity
            {
                MediaItemId = mediaItemId,
                Title = chapter.Title ?? string.Empty,
                StartSeconds = chapter.StartSeconds,
                CreatedAt = chapter.CreatedAt == default ? now : chapter.CreatedAt,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AiChapterEntity>> GetChaptersAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AiChapters.AsNoTracking()
            .Where(c => c.MediaItemId == mediaItemId)
            .OrderBy(c => c.StartSeconds)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (artifact.CreatedAt == default)
        {
            artifact.CreatedAt = DateTime.UtcNow;
        }

        db.AiArtifacts.Add(artifact);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return artifact;
    }

    public async Task<IReadOnlyList<AiArtifact>> GetArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.AiArtifacts.AsNoTracking()
            .Where(a => a.MediaItemId == mediaItemId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.AiArtifacts.Where(a => a.MediaItemId == mediaItemId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
