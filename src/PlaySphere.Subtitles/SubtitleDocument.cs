namespace PlaySphere.Subtitles;

/// <summary>
/// A parsed subtitle track: an ordered, non-overlapping-by-index cue list plus the format
/// it was parsed from. Cues are kept in source order; callers needing time-sorted lookup
/// should sort explicitly since malformed sources may not be monotonic.
/// </summary>
public sealed record SubtitleDocument(SubtitleFormat Format, IReadOnlyList<SubtitleCue> Cues)
{
    public static SubtitleDocument Empty(SubtitleFormat format) => new(format, Array.Empty<SubtitleCue>());

    /// <summary>Cues active at <paramref name="position"/>, applying <paramref name="delay"/> (per PICKLE.md §17 subtitle delay).</summary>
    public IEnumerable<SubtitleCue> CuesAt(TimeSpan position, TimeSpan delay = default)
    {
        foreach (var cue in Cues)
        {
            var start = cue.Start + delay;
            var end = cue.End + delay;
            if (position >= start && position < end)
            {
                yield return cue;
            }
        }
    }

    /// <summary>
    /// Cues whose text contains <paramref name="query"/> (case-insensitive substring match),
    /// in cue order (PICKLE.md §17 "cue search"). An empty or whitespace-only query returns
    /// no cues rather than the entire document.
    /// </summary>
    public IEnumerable<SubtitleCue> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        foreach (var cue in Cues)
        {
            if (cue.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                yield return cue;
            }
        }
    }
}
