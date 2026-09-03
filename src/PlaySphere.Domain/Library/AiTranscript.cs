namespace PlaySphere.Domain.Library;

using System.Collections.Generic;

/// <summary>
/// A persisted AI transcript for a cataloged media item (PICKLE.md §42 table <c>transcripts</c>,
/// AI-03/04). Holds one transcript per media item (latest wins) plus the language the provider
/// reported and the model/provider that produced it, so it can be regenerated with a different
/// language without ambiguity. Content is untrusted media-derived input (PICKLE.md §43).
/// </summary>
public sealed class AiTranscript
{
    public int Id { get; set; }

    /// <summary>The cataloged <see cref="LibraryMediaItem"/> this transcript belongs to.</summary>
    public int MediaItemId { get; set; }

    /// <summary>The language the provider reported (e.g. "en"); null when unknown.</summary>
    public string? Language { get; set; }

    /// <summary>Model/provider that produced it (e.g. "whisper-1"); for diagnostic display.</summary>
    public string? SourceModel { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> (see <see cref="LibraryMediaItem.LastPlayedAt"/>).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The ordered segments, populated only when a transcript is loaded via the repository
    /// (<see cref="PlaySphere.Application.Library.IMediaLibraryRepository.GetTranscriptAsync"/>).
    /// Not mapped to a column — segments live in the <c>transcript_segments</c> table.
    /// </summary>
    public IReadOnlyList<AiTranscriptSegment>? Segments { get; set; }
}

