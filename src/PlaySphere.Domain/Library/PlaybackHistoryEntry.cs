namespace PlaySphere.Domain.Library;

/// <summary>One playback session recorded against a <see cref="LibraryMediaItem"/> (PICKLE.md §42).</summary>
public sealed class PlaybackHistoryEntry
{
    public int Id { get; set; }

    public int MediaItemId { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> rather than <see cref="DateTimeOffset"/> —
    /// the EF Core SQLite provider cannot translate ORDER BY over DateTimeOffset columns.</summary>
    public DateTime PlayedAt { get; set; }

    /// <summary>Position reached when this session ended.</summary>
    public TimeSpan Position { get; set; }

    /// <summary>Media duration at the time of this session, for percentage calculations.</summary>
    public TimeSpan Duration { get; set; }
}
