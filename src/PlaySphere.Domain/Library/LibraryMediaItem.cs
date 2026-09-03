namespace PlaySphere.Domain.Library;

/// <summary>
/// A cataloged media item in the user's library (PICKLE.md §22/§23). Distinct from
/// <see cref="Media.MediaFile"/>, which is the lightweight value the playback engine loads —
/// this is the persisted catalog row: technical metadata, playback progress and favorites.
/// </summary>
public sealed class LibraryMediaItem
{
    public int Id { get; set; }

    /// <summary>Absolute, canonicalized path to the media file.</summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Identity fingerprint combining file length, modified timestamp and a sampled hash
    /// (PICKLE.md §24 — never path alone, so a renamed/moved file can still be recognized).
    /// </summary>
    public required string Fingerprint { get; set; }

    public string? Title { get; set; }

    public TimeSpan Duration { get; set; }

    public string? Resolution { get; set; }

    public string? Codec { get; set; }

    public double? FrameRate { get; set; }

    public long? BitrateKbps { get; set; }

    /// <summary>Number of audio tracks the last probe found (detail lives in live playback metadata, not here).</summary>
    public int AudioTrackCount { get; set; }

    /// <summary>Number of subtitle tracks the last probe found.</summary>
    public int SubtitleTrackCount { get; set; }

    public string? Album { get; set; }

    public string? Artist { get; set; }

    /// <summary>Path to a cached cover-art/thumbnail image, if one has been extracted.</summary>
    public string? CoverArtPath { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> rather than <see cref="DateTimeOffset"/> —
    /// the EF Core SQLite provider cannot translate ORDER BY over DateTimeOffset columns.</summary>
    public DateTime? LastPlayedAt { get; set; }

    public TimeSpan LastPosition { get; set; }

    /// <summary>0–100. Drives "Continue Watching" (partially watched) vs. considered finished.</summary>
    public double WatchedPercentage { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>The library folder this item was discovered under.</summary>
    public int LibraryFolderId { get; set; }
}
