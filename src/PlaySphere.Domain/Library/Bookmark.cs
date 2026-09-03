namespace PlaySphere.Domain.Library;

/// <summary>
/// A user bookmark on a cataloged media item (PICKLE.md §21). Clicking a bookmark seeks the
/// player to its timestamp. Bookmarks belong to a media item and carry optional label/note/category.
/// </summary>
public sealed class Bookmark
{
    public int Id { get; set; }

    /// <summary>The cataloged <see cref="LibraryMediaItem"/> this bookmark is attached to.</summary>
    public int MediaItemId { get; set; }

    /// <summary>The timestamp the player seeks to when the bookmark is clicked (milliseconds).</summary>
    public long PositionMs { get; set; }

    public string? Label { get; set; }

    public string? Note { get; set; }

    public string? Category { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime CreatedAt { get; set; }
}
