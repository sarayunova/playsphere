using Microsoft.EntityFrameworkCore;
using PlaySphere.Domain.Library;

namespace PlaySphere.Infrastructure.Library;

/// <summary>EF Core context for the SQLite library database (PICKLE.md §4/§42).</summary>
public sealed class PlaySphereDbContext(DbContextOptions<PlaySphereDbContext> options) : DbContext(options)
{
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();

    public DbSet<LibraryMediaItem> MediaItems => Set<LibraryMediaItem>();

    public DbSet<PlaybackHistoryEntry> PlaybackHistory => Set<PlaybackHistoryEntry>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();

    public DbSet<AiTranscript> AiTranscripts => Set<AiTranscript>();

    public DbSet<AiTranscriptSegment> AiTranscriptSegments => Set<AiTranscriptSegment>();

    public DbSet<AiChapterEntity> AiChapters => Set<AiChapterEntity>();

    public DbSet<AiArtifact> AiArtifacts => Set<AiArtifact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibraryFolder>(folder =>
        {
            folder.ToTable("library_folders");
            folder.Property(f => f.Path).IsRequired();
            folder.HasIndex(f => f.Path).IsUnique();
        });

        modelBuilder.Entity<LibraryMediaItem>(item =>
        {
            item.ToTable("media_items");
            item.Property(m => m.FilePath).IsRequired();
            item.Property(m => m.Fingerprint).IsRequired();
            item.HasIndex(m => m.Fingerprint).IsUnique();
            item.HasIndex(m => m.FilePath);
            item.HasOne<LibraryFolder>()
                .WithMany()
                .HasForeignKey(m => m.LibraryFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaybackHistoryEntry>(history =>
        {
            history.ToTable("playback_history");
            history.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(h => h.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bookmark>(bookmark =>
        {
            bookmark.ToTable("bookmarks");
            bookmark.Property(b => b.MediaItemId).IsRequired();
            bookmark.Property(b => b.PositionMs).IsRequired();
            bookmark.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(b => b.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
            bookmark.HasIndex(b => new { b.MediaItemId, b.PositionMs });
        });

        modelBuilder.Entity<Playlist>(playlist =>
        {
            playlist.ToTable("playlists");
            playlist.Property(p => p.Name).IsRequired();
        });

        modelBuilder.Entity<PlaylistItem>(item =>
        {
            item.ToTable("playlist_items");
            item.Property(i => i.PlaylistId).IsRequired();
            item.Property(i => i.MediaItemId).IsRequired();
            item.Property(i => i.Position).IsRequired();
            item.HasOne<Playlist>()
                .WithMany()
                .HasForeignKey(i => i.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
            item.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(i => i.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
            item.HasIndex(i => new { i.PlaylistId, i.Position });
        });

        modelBuilder.Entity<AiTranscript>(transcript =>
        {
            transcript.ToTable("transcripts");
            transcript.Property(t => t.MediaItemId).IsRequired();
            transcript.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(t => t.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
            // One transcript per media item (latest wins) keeps the UI simple and unambiguous.
            transcript.HasIndex(t => t.MediaItemId).IsUnique();
            // Segments are loaded separately from the transcript_segments table (see GetTranscriptAsync).
            transcript.Ignore(t => t.Segments);
        });

        modelBuilder.Entity<AiTranscriptSegment>(segment =>
        {
            segment.ToTable("transcript_segments");
            segment.Property(s => s.TranscriptId).IsRequired();
            segment.Property(s => s.Text).IsRequired();
            segment.HasOne<AiTranscript>()
                .WithMany()
                .HasForeignKey(s => s.TranscriptId)
                .OnDelete(DeleteBehavior.Cascade);
            segment.HasIndex(s => new { s.TranscriptId, s.Ordinal });
        });

        modelBuilder.Entity<AiChapterEntity>(chapter =>
        {
            chapter.ToTable("chapters");
            chapter.Property(c => c.MediaItemId).IsRequired();
            chapter.Property(c => c.Title).IsRequired();
            chapter.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(c => c.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
            chapter.HasIndex(c => new { c.MediaItemId, c.StartSeconds });
        });

        modelBuilder.Entity<AiArtifact>(artifact =>
        {
            artifact.ToTable("ai_artifacts");
            artifact.Property(a => a.MediaItemId).IsRequired();
            artifact.Property(a => a.Key).IsRequired();
            artifact.Property(a => a.PayloadJson).IsRequired();
            artifact.HasOne<LibraryMediaItem>()
                .WithMany()
                .HasForeignKey(a => a.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
            artifact.HasIndex(a => new { a.MediaItemId, a.Key });
        });
    }
}
