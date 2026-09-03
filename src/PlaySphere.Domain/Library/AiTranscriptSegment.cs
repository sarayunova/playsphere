namespace PlaySphere.Domain.Library;

/// <summary>
/// A single timed transcript segment, persisted in order (PICKLE.md §42 table
/// <c>transcript_segments</c>, AI-03/04). Start/end are in seconds. Drives the transcript viewer,
/// timestamp search and clickable timestamp evidence (PICKLE.md §41). Text is untrusted (PICKLE.md §43).
/// </summary>
public sealed class AiTranscriptSegment
{
    public int Id { get; set; }

    /// <summary>The <see cref="AiTranscript"/> this segment belongs to.</summary>
    public int TranscriptId { get; set; }

    /// <summary>Zero-based order within the transcript; the viewer renders in this order.</summary>
    public int Ordinal { get; set; }

    public double StartSeconds { get; set; }

    public double EndSeconds { get; set; }

    public string Text { get; set; } = string.Empty;
}
