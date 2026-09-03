using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlaySphere.Domain.AI;

/// <summary>
/// A single contiguous transcript segment with its start/end timestamps (PICKLE.md §41: answers
/// must return clickable timestamp evidence). Sourced from an AI transcription; treated as untrusted
/// until validated (PICKLE.md §43).
/// </summary>
public sealed record TranscriptSegment(
    double StartSeconds,
    double EndSeconds,
    string Text)
{
    public string ToTimestamp() =>
        $"{Format(StartSeconds)} - {Format(EndSeconds)}";

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}

/// <summary>
/// The full transcript of a media item: an ordered, read-only list of timed segments plus the raw
/// language tag reported by the provider (e.g. "en"). All OpenAI-derived content is untrusted.
/// </summary>
public sealed class Transcript
{
    public Transcript(IReadOnlyList<TranscriptSegment> segments, string? language)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = new ReadOnlyCollection<TranscriptSegment>(segments.ToList());
        Language = language;
    }

    public IReadOnlyList<TranscriptSegment> Segments { get; }

    public string? Language { get; }

    public bool IsEmpty => Segments.Count == 0;
}
