namespace PlaySphere.Domain.Library;

/// <summary>
/// A generic persisted AI artifact for a cataloged media item (PICKLE.md §42 table
/// <c>ai_artifacts</c>, AI-07/08). <see cref="Key"/> discriminates the artifact kind (e.g.
/// "summary", "keypoint") and <see cref="PayloadJson"/> holds the JSON-serialized content, so the
/// schema stays stable as new AI artifact types arrive without a migration per kind. Nullable
/// <see cref="StartSeconds"/> carries optional timestamp evidence (PICKLE.md §41). Payload is
/// untrusted media-derived input (PICKLE.md §43).
/// </summary>
public sealed class AiArtifact
{
    public int Id { get; set; }

    /// <summary>The cataloged <see cref="LibraryMediaItem"/> this artifact belongs to.</summary>
    public int MediaItemId { get; set; }

    /// <summary>Discriminates the artifact kind (e.g. "summary", "keypoint"), so multiple kinds coexist per item.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>JSON-serialized artifact content.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Optional timestamp evidence in seconds when the artifact references a specific point.</summary>
    public double? StartSeconds { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime CreatedAt { get; set; }
}
