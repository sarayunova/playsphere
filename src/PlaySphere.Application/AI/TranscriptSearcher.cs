using System;
using System.Collections.Generic;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;

namespace PlaySphere.Application.AI;

/// <summary>
/// Pure, untrusted-safe timestamp search over a transcript (AI-05, PICKLE.md §21 "timestamp
/// search": find a spoken moment in the transcript and jump to it). Case-insensitive substring
/// match on the segment text; empty/whitespace queries return no results. Results are returned in
/// transcript order. The two overloads cover the two shapes a transcript takes at runtime — the
/// in-memory <see cref="Domain.AI.TranscriptSegment"/> produced by a live transcription, and the
/// persisted <see cref="Domain.Library.AiTranscriptSegment"/> loaded from the repository.
/// </summary>
public static class TranscriptSearcher
{
    /// <summary>Returns transcript segments whose text contains <paramref name="rawQuery"/> (case-insensitive), in order.</summary>
    public static IReadOnlyList<TranscriptSegment> Search(Transcript transcript, string? rawQuery)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        var query = Normalize(rawQuery);
        if (query.Length == 0)
        {
            return [];
        }

        var results = new List<TranscriptSegment>();
        foreach (var segment in transcript.Segments)
        {
            if (segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(segment);
            }
        }
        return results;
    }

    /// <summary>Returns persisted transcript segments whose text contains <paramref name="rawQuery"/> (case-insensitive), in order.</summary>
    public static IReadOnlyList<AiTranscriptSegment> Search(IReadOnlyList<AiTranscriptSegment> segments, string? rawQuery)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var query = Normalize(rawQuery);
        if (query.Length == 0)
        {
            return [];
        }

        var results = new List<AiTranscriptSegment>();
        foreach (var segment in segments)
        {
            if (segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(segment);
            }
        }
        return results;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        return value.Trim();
    }
}
