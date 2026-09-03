namespace PlaySphere.Domain.Library;

/// <summary>
/// One cataloged media item in a <see cref="Playlist"/>, in playlist order (PICKLE.md §25).
/// <see cref="Position"/> is the zero-based playback order within the playlist.
/// </summary>
public sealed class PlaylistItem
{
    public int Id { get; set; }

    /// <summary>The owning <see cref="Playlist"/>.</summary>
    public int PlaylistId { get; set; }

    /// <summary>The cataloged <see cref="LibraryMediaItem"/> referenced by this entry.</summary>
    public int MediaItemId { get; set; }

    /// <summary>Zero-based ordering within the playlist.</summary>
    public int Position { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime AddedAt { get; set; }
}
