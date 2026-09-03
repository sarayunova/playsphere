namespace PlaySphere.Domain.Library;

/// <summary>
/// A persisted auto-generated chapter for a cataloged media item (PICKLE.md §42 table
/// <c>chapters</c>, AI-06). Distinct from the <c>Domain.AI.AiChapter</c> result record — this is the
/// persisted row attached to a media item, overridable with a user title later. Content is untrusted
/// (PICKLE.md §43). The entity class name carries an <c>Entity</c> suffix to avoid clashing with the
/// immutable <see cref="AI.AiChapter"/> record.
/// </summary>
public sealed class AiChapterEntity
{
    public int Id { get; set; }

    /// <summary>The cataloged <see cref="LibraryMediaItem"/> this chapter belongs to.</summary>
    public int MediaItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public double StartSeconds { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime CreatedAt { get; set; }
}
