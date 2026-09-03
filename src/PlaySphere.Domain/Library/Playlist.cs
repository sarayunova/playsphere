namespace PlaySphere.Domain.Library;

/// <summary>
/// A user-created playlist (PICKLE.md §25). Persists the ordered set of cataloged media items via
/// <see cref="PlaylistItem"/> rows. Player-state concerns (shuffle/repeat) live in the playback
/// session, not here.
/// </summary>
public sealed class Playlist
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime CreatedAt { get; set; }
}
